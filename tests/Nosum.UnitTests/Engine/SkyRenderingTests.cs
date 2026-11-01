// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>The commands appended for docs/adr/ADR-027: ambient tint, light sources and coloured rectangles, and the rain/snow layer.</summary>
public sealed class SkyRenderingTests
{
    private static int[] Ints(RenderCommandBuffer buffer) => MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();

    [Fact]
    public void SetAmbient_RecordsOneCommandWithTheColourInTheSourceFields()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(2);

        buffer.SetAmbient(18, 24, 64, 168);

        Ints(buffer).Should().Equal(1, 2, RenderCommandBuffer.AmbientTexture, 18, 24, 64, 168, 0, 0, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void SetAmbient_WithNoOpacity_RecordsNothing()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(2);

        buffer.SetAmbient(10, 10, 10, 0);

        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void SetAmbient_ClampsEachChannel()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(1);

        buffer.SetAmbient(900, -4, 255, 4000);

        Ints(buffer).Skip(3).Take(4).Should().Equal(255, 0, 255, 255);
    }

    [Fact]
    public void AddLight_RecordsCentreAndRadius()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(3);

        buffer.AddLight(120, 80, 56).Should().BeTrue();

        Ints(buffer).Should().Equal(1, 3, RenderCommandBuffer.LightTexture, 0, 0, 56, 0, 120, 80, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void AddLight_PastTheLightCap_IsDroppedAndTheCapResetsEachFrame()
    {
        RenderCommandBuffer buffer = new(200);
        buffer.Begin(1);

        for (int i = 0; i < RenderCommandBuffer.MaxLights + 10; i++)
        {
            _ = buffer.AddLight(i, i, 10);
        }

        buffer.LightCount.Should().Be(RenderCommandBuffer.MaxLights);
        buffer.Count.Should().Be(RenderCommandBuffer.MaxLights);

        buffer.Begin(1);
        buffer.AddLight(1, 1, 10).Should().BeTrue();
        buffer.LightCount.Should().Be(1);
    }

    [Fact]
    public void AddLight_WithNoRadiusOrNoRoom_IsNotRecorded()
    {
        RenderCommandBuffer buffer = new(1);
        buffer.Begin(1);

        buffer.AddLight(0, 0, 0).Should().BeFalse();
        buffer.AddLight(0, 0, 8).Should().BeTrue();
        buffer.AddLight(0, 0, 8).Should().BeFalse("the buffer is full");
    }

    [Fact]
    public void FillRect_RecordsTheColourInTheTint_AndSkipsInvisibleOnes()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(1);

        buffer.FillRect(5, 6, 1, 4, unchecked((int)0xB4CCFFC0));
        buffer.FillRect(5, 6, 0, 4, unchecked((int)0xB4CCFFC0));
        buffer.FillRect(5, 6, 1, 4, unchecked((int)0xB4CCFF00));

        buffer.Count.Should().Be(1);
        Ints(buffer).Should().Equal(1, 1, RenderCommandBuffer.FillTexture, 0, 0, 1, 4, 5, 6, 0, unchecked((int)0xB4CCFFC0));
    }

    [Fact]
    public void NewCommandIds_AreDistinctFromTheOlderOnes()
    {
        int[] ids = [RenderCommandBuffer.BlackFill, RenderCommandBuffer.LabelTexture, RenderCommandBuffer.AmbientTexture, RenderCommandBuffer.LightTexture, RenderCommandBuffer.FillTexture];

        ids.Distinct().Should().HaveCount(ids.Length);
        ids.Should().OnlyContain(id => id < 0);
        (RenderCommandBuffer.BlackFill, RenderCommandBuffer.LabelTexture).Should().Be((-2, -3), "the contract is append-only");
    }

    [Fact]
    public void Precipitation_None_DrawsNothing()
    {
        PrecipitationLayer layer = new();
        RenderCommandBuffer buffer = new(PrecipitationLayer.Capacity);
        buffer.Begin(2);

        layer.Update(Precipitation.None, 0.016, 416, 234);
        layer.Draw(Precipitation.None, buffer);

        layer.ActiveCount.Should().Be(0);
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Precipitation_Storm_DrawsEachDropAsTwoSegments_SoItLeansInsteadOfFallingUpright()
    {
        PrecipitationLayer storm = new();
        RenderCommandBuffer stormDraws = new(PrecipitationLayer.Capacity * 3);
        stormDraws.Begin(2);
        storm.Update(Precipitation.Storm, 0.016, 416, 234);
        storm.Draw(Precipitation.Storm, stormDraws);

        // A rain drop is one rectangle (plus a splash on the bottom edge); a storm drop is two, stepped a pixel sideways, except the few at the bottom edge.
        stormDraws.Count.Should().BeGreaterThan(storm.ActiveCount + (storm.ActiveCount / 2));
    }

    [Fact]
    public void Precipitation_Rain_DrawsOneRectanglePerDrop()
    {
        PrecipitationLayer rain = new();
        RenderCommandBuffer rainDraws = new(PrecipitationLayer.Capacity);
        rainDraws.Begin(2);
        rain.Update(Precipitation.Rain, 0.016, 416, 234);
        rain.Draw(Precipitation.Rain, rainDraws);

        rainDraws.Count.Should().BeLessThan(2 * rain.ActiveCount);
    }

    [Theory]
    [InlineData(Precipitation.Rain)]
    [InlineData(Precipitation.Storm)]
    [InlineData(Precipitation.Snow)]
    public void Precipitation_Falling_DrawsParticlesInsideTheViewAndNeverOverflowsThePool(Precipitation kind)
    {
        PrecipitationLayer layer = new();

        // A drop that reaches the bottom also draws a splash (docs/adr/ADR-071 D5), so a frame holds the particles and at most as many splashes.
        // A storm drop is two segments (docs/adr/ADR-027 D6), so a storm frame holds at most three draws per drop.
        int perDrop = kind == Precipitation.Storm ? 3 : 2;
        RenderCommandBuffer buffer = new(PrecipitationLayer.Capacity * perDrop);
        for (int frame = 0; frame < 600; frame++)
        {
            layer.Update(kind, 1.0 / 60, 416, 234);
            buffer.Begin(perDrop);
            layer.Draw(kind, buffer);
            buffer.Count.Should().BeGreaterThanOrEqualTo(layer.ActiveCount).And.BeLessThanOrEqualTo(perDrop * layer.ActiveCount);
        }

        int[] ints = Ints(buffer);
        buffer.Count.Should().BeGreaterThan(20);
        for (int i = 0; i < buffer.Count; i++)
        {
            int p = RenderCommandBuffer.HeaderInts + (i * RenderCommandBuffer.IntsPerCommand);
            ints[p].Should().Be(RenderCommandBuffer.FillTexture);
            ints[p + 5].Should().BeInRange(0, 415);
            ints[p + 6].Should().BeInRange(-8, 233);
        }
    }

    [Fact]
    public void Precipitation_Snow_NearFlakesAreTwoPixelsSquareAndFarOnesOne()
    {
        // A near flake (depth above one half) is 2x2 so it reads on grass; a far one stays 1x1 (docs/adr/ADR-027 amendment, round 2).
        PrecipitationLayer snow = new();
        RenderCommandBuffer draws = new(PrecipitationLayer.Capacity * 2);
        draws.Begin(2);
        snow.Update(Precipitation.Snow, 0.016, 416, 234);
        snow.Draw(Precipitation.Snow, draws);

        int[] ints = Ints(draws);
        int sizes = 0;
        for (int i = 0; i < draws.Count; i++)
        {
            int p = RenderCommandBuffer.HeaderInts + (i * RenderCommandBuffer.IntsPerCommand);
            int width = ints[p + 3];
            int height = ints[p + 4];
            width.Should().Be(height, "snow flakes are squares");
            width.Should().BeOneOf(1, 2);
            sizes |= width == 2 ? 2 : 1;
        }

        sizes.Should().Be(3, "the snow has both near (2 px) and far (1 px) flakes");
    }

    [Fact]
    public void Precipitation_StormIsDenserThanRainAndRainThanSnow()
    {
        PrecipitationLayer storm = new(), rain = new(), snow = new();

        storm.Update(Precipitation.Storm, 0.016, 416, 234);
        rain.Update(Precipitation.Rain, 0.016, 416, 234);
        snow.Update(Precipitation.Snow, 0.016, 416, 234);

        storm.ActiveCount.Should().BeGreaterThan(rain.ActiveCount);
        rain.ActiveCount.Should().BeGreaterThan(snow.ActiveCount);
    }

    [Fact]
    public void Precipitation_SameStart_FallsTheSameWay()
    {
        PrecipitationLayer a = new(), b = new();
        RenderCommandBuffer first = new(PrecipitationLayer.Capacity), second = new(PrecipitationLayer.Capacity);

        for (int i = 0; i < 90; i++)
        {
            a.Update(Precipitation.Rain, 1.0 / 60, 300, 200);
            b.Update(Precipitation.Rain, 1.0 / 60, 300, 200);
        }

        first.Begin(1);
        second.Begin(1);
        a.Draw(Precipitation.Rain, first);
        b.Draw(Precipitation.Rain, second);

        Ints(first).Should().Equal(Ints(second));
    }

    [Fact]
    public void Precipitation_AFrame_AllocatesNothing()
    {
        PrecipitationLayer layer = new();
        RenderCommandBuffer buffer = new(PrecipitationLayer.Capacity);
        // Warm every path the measured frames take (first calls load types and compile methods on this thread). Three frames are not enough: the slowest
        // particle needs about 95 frames to reach the bottom, and the wrap-around branch (a particle re-seeded through Next) runs for the first time then,
        // inside the measured window, where its first-call cost is counted as an allocation of this thread.
        for (int warm = 0; warm < 300; warm++)
        {
            layer.Update(Precipitation.Rain, 0.016, 416, 234);
            buffer.Begin(2);
            layer.Draw(Precipitation.Rain, buffer);
            _ = buffer.Encode();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            layer.Update(Precipitation.Rain, 0.016, 416, 234);
            buffer.Begin(2);
            layer.Draw(Precipitation.Rain, buffer);
            _ = buffer.Encode();
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }
}
