// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Runtime.InteropServices;
using FluentAssertions;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>The flecks of an action (backlog item 9): a fixed pool, lives that end, and reduced motion that does not move them.</summary>
public sealed class FleckBurstTests
{
    private static readonly FleckStyle Style = new(Rgb: 0xFFD84D, StartSize: 3, EndSize: 2, LifeMs: 200, TravelPx: 8, LiftPx: 6);

    [Fact]
    public void Spawn_ThenAddTo_AddsOneSpritePerFleck()
    {
        FleckBurst burst = new();
        WorldSpriteBatch world = new(FleckBurst.Capacity);
        world.Begin();

        burst.Spawn(100, 100, 101, 6, Style, nowMs: 1000, reduced: false, seed: 0);
        burst.AddTo(world, nowMs: 1050);

        world.Count.Should().Be(6);
    }

    [Fact]
    public void AddTo_AfterTheLife_RetiresEveryFleck()
    {
        FleckBurst burst = new();
        WorldSpriteBatch world = new(FleckBurst.Capacity);
        world.Begin();
        burst.Spawn(100, 100, 101, 6, Style, nowMs: 1000, reduced: false, seed: 0);

        burst.CountLive(nowMs: 1200).Should().Be(0);
        burst.AddTo(world, nowMs: 1200);
        world.Count.Should().Be(0);
    }

    [Fact]
    public void Spawn_OverTheCapacity_OverwritesTheOldestFlecks()
    {
        FleckBurst burst = new();
        for (int i = 0; i < 20; i++)
        {
            burst.Spawn(0, 0, 0, 6, Style, nowMs: 1000, reduced: false, seed: i);
        }

        burst.CountLive(nowMs: 1000).Should().Be(FleckBurst.Capacity);
    }

    [Fact]
    public void Spawn_CountOverTheCapacity_IsCappedAtTheCapacity()
    {
        FleckBurst burst = new();
        burst.Spawn(0, 0, 0, FleckBurst.Capacity + 50, Style, nowMs: 1000, reduced: false, seed: 0);

        burst.CountLive(nowMs: 1000).Should().Be(FleckBurst.Capacity);
    }

    [Fact]
    public void Spawn_ReducedMotion_FleckStaysAtItsStartPoint()
    {
        FleckBurst burst = new();
        WorldSpriteBatch world = new(FleckBurst.Capacity);
        world.Begin();
        burst.Spawn(100, 100, 101, 1, Style, nowMs: 1000, reduced: true, seed: 2);
        RenderCommandBuffer commands = new(16);
        commands.Begin(1);

        burst.AddTo(world, nowMs: 1150);
        world.Flush(commands, Camera());

        int[] ints = Ints(commands);
        ints[RenderCommandBuffer.HeaderInts + 5].Should().BeInRange(99, 101, "the fleck fades in place");
        ints[RenderCommandBuffer.HeaderInts + 6].Should().BeInRange(99, 101);
    }

    [Fact]
    public void AddTo_MidLife_FadesTheTint()
    {
        FleckBurst burst = new();
        WorldSpriteBatch world = new(FleckBurst.Capacity);
        world.Begin();
        burst.Spawn(100, 100, 101, 1, Style, nowMs: 1000, reduced: false, seed: 0);
        RenderCommandBuffer commands = new(16);
        commands.Begin(1);

        burst.AddTo(world, nowMs: 1100);
        world.Flush(commands, Camera());

        int tint = Ints(commands)[RenderCommandBuffer.HeaderInts + 8];
        (tint & 0xFF).Should().BeInRange(120, 135, "half the life is half the alpha");
        ((tint >>> 8) & 0xFFFFFF).Should().Be(0xFFD84D);
    }

    private static Camera2D Camera()
    {
        Camera2D camera = default;
        camera.Follow(500, 500, 1000, 1000, 2000, 2000);
        return camera;
    }

    private static int[] Ints(RenderCommandBuffer commands) => MemoryMarshal.Cast<byte, int>(commands.Encode().ToArray()).ToArray();
}
