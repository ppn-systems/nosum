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

    [Theory]
    [InlineData(Precipitation.Rain)]
    [InlineData(Precipitation.Storm)]
    [InlineData(Precipitation.Snow)]
    public void Precipitation_Falling_DrawsParticlesInsideTheViewAndNeverOverflowsThePool(Precipitation kind)
    {
        PrecipitationLayer layer = new();
        RenderCommandBuffer buffer = new(PrecipitationLayer.Capacity);

        for (int frame = 0; frame < 600; frame++)
        {
            layer.Update(kind, 1.0 / 60, 416, 234);
            buffer.Begin(2);
            layer.Draw(kind, buffer);
            buffer.Count.Should().Be(layer.ActiveCount).And.BeLessThanOrEqualTo(PrecipitationLayer.Capacity);
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
        // Warm every call the measured frames make (first calls load types and compile methods on this thread).
        for (int warm = 0; warm < 3; warm++)
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
