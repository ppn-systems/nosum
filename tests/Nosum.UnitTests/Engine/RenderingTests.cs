// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.InteropServices;
using FluentAssertions;
using Nosum.Core;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class RenderingTests
{
    [Fact]
    public void Encode_TwoDraws_WritesHeaderThenNineIntsEach()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(scale: 3);
        buffer.Draw(7, 1, 2, 16, 32, 100, 200, RenderCommandBuffer.FlipX, 0x11223344);
        buffer.Draw(8, 0, 0, 16, 16, -5, 6);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();

        ints.Should().Equal(2, 3, 7, 1, 2, 16, 32, 100, 200, 1, 0x11223344, 8, 0, 0, 16, 16, -5, 6, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void Draw_PastCapacity_IsDropped()
    {
        RenderCommandBuffer buffer = new(1);
        buffer.Begin(1);

        buffer.Draw(1, 0, 0, 1, 1, 0, 0);
        buffer.Draw(2, 0, 0, 1, 1, 0, 0);

        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void Follow_NearWorldEdge_ClampsInsideWorld()
    {
        Camera2D camera = default;

        camera.Follow(5, 5, 100, 50, 400, 300);

        camera.Left.Should().Be(0);
        camera.Top.Should().Be(0);
    }

    [Fact]
    public void Follow_Middle_CentresOnTarget()
    {
        Camera2D camera = default;

        camera.Follow(200, 150, 100, 50, 400, 300);

        camera.Left.Should().Be(150);
        camera.Top.Should().Be(125);
    }

    [Fact]
    public void TileLayerRenderer_Draw_SkipsEmptyAndOffscreenTiles()
    {
        TileMap map = new(4, 1, 16, 16, [new TileSet(1, "t", "t", 2, 4)], [new TileLayer("Back", [1, 0, 2, 3])]);
        Camera2D camera = default;
        camera.Follow(16, 8, 32, 16, 64, 16); // shows tiles 0..1 wide view starting at x=0
        RenderCommandBuffer buffer = new(16);
        buffer.Begin(1);

        TileLayerRenderer.Draw(map, map.Layers[0], [5], camera, buffer);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        buffer.Count.Should().Be(1);
        ints[2..9].Should().Equal(5, 0, 0, 16, 16, 0, 0);
    }
}
