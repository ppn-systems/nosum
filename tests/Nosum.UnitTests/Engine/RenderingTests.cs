// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public void Follow_SmallWorldWithBottomInset_IsCentredAboveTheStrip()
    {
        Camera2D camera = default;

        camera.Follow(96, 96, 426, 240, 192, 192, 40);

        camera.Top.Should().Be(-4); // (192 - (240 - 40)) / 2: 4 world pixels free above and below
        camera.Left.Should().Be(-117);
    }

    [Fact]
    public void Follow_WorldTallerThanTheFreeArea_IgnoresTheInset()
    {
        Camera2D camera = default;

        camera.Follow(200, 150, 100, 50, 400, 300, 10);

        camera.Top.Should().Be(125);
    }

    [Theory]
    [InlineData(1280, 720, 120, 3)] // a 12 x 12 tile room, hotbar strip 40 u at u = 3
    [InlineData(1920, 1080, 200, 4)] // at 5 the room (960 px) would sit under the hotbar strip (200 px)
    [InlineData(1600, 900, 160, 3)]
    public void ViewZoom_Room_FitsWholeAboveTheStrip(int width, int height, int inset, int expected)
        => ViewZoom.Pick(192, 192, 16, width, height, inset, 26, 6).Should().Be(expected);

    [Theory]
    [InlineData(1280, 720, 3)] // the farm: 80 x 65 tiles, the open-map zoom
    [InlineData(1920, 1080, 5)]
    public void ViewZoom_OpenMap_UsesTheOpenMapZoom(int width, int height, int expected)
        => ViewZoom.Pick(80 * 16, 65 * 16, 16, width, height, 40 * expected, 26, 6).Should().Be(expected);

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

    [Fact]
    public void TileLayerRenderer_Draw_AnimatedTile_UsesFrameForTime()
    {
        TileAnimation water = new([0, 3], [1000, 1000]);
        TileSet set = new(1, "t", "t", 2, 4, new TileProperties(4, new Dictionary<int, KeyValuePair<string, string>[]>(), new Dictionary<int, TileAnimation> { [0] = water }));
        TileMap map = new(1, 1, 16, 16, [set], [new TileLayer("Back", [1])]);
        Camera2D camera = default;
        camera.Follow(8, 8, 16, 16, 16, 16);
        RenderCommandBuffer buffer = new(4);

        buffer.Begin(1);
        TileLayerRenderer.Draw(map, map.Layers[0], [0], camera, buffer, 0);
        int[] first = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        buffer.Begin(1);
        TileLayerRenderer.Draw(map, map.Layers[0], [0], camera, buffer, 1000);
        int[] second = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();

        first[3..5].Should().Equal(0, 0);
        second[3..5].Should().Equal(16, 16); // local tile 3 in a 2-column sheet
    }

    [Fact]
    public void Flush_Sprites_DrawInAscendingYThenXThenId()
    {
        WorldSpriteBatch batch = new(8);
        Camera2D camera = default;
        camera.Follow(50, 50, 100, 100, 100, 100);
        RenderCommandBuffer buffer = new(8);
        buffer.Begin(1);
        batch.Begin();
        batch.Add(1, 0, 0, 4, 4, 0, 0, sortY: 30, sortX: 5);
        batch.Add(2, 0, 0, 4, 4, 0, 0, sortY: 10, sortX: 9);
        batch.Add(3, 0, 0, 4, 4, 0, 0, sortY: 10, sortX: 2, id: 7);
        batch.Add(4, 0, 0, 4, 4, 0, 0, sortY: 10, sortX: 2, id: 3);
        batch.Add(5, 0, 0, 4, 4, 0, 0, sortY: 10, sortX: 2, id: 3);

        batch.Flush(buffer, camera);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        int[] textures = [ints[2], ints[11], ints[20], ints[29], ints[38]];
        textures.Should().Equal(4, 5, 3, 2, 1);
        batch.Count.Should().Be(0);
    }

    [Fact]
    public void Flush_DeferLabels_EmitsLabelsOnlyOnFlushLabelsAfterFrontDraws()
    {
        WorldSpriteBatch batch = new(8);
        Camera2D camera = default;
        camera.Follow(50, 50, 100, 100, 100, 100);
        RenderCommandBuffer buffer = new(8);
        buffer.Begin(1);
        batch.Begin();
        batch.Add(RenderCommandBuffer.LabelTexture, 42, 0, 0, 0, 20, 10, sortY: 15, sortX: 20, id: 1);
        batch.Add(7, 0, 0, 16, 16, 16, 16, sortY: 32, sortX: 16, id: 2);

        batch.Flush(buffer, camera, deferLabels: true);
        buffer.Draw(99, 0, 0, 16, 16, 0, 0); // Front layer / ambient darkness draw
        batch.FlushLabels(buffer, camera);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        buffer.Count.Should().Be(3);
        int[] textures = [ints[2], ints[11], ints[20]];
        textures.Should().Equal(7, 99, RenderCommandBuffer.LabelTexture);
        ints[21].Should().Be(42, "the label id is preserved in srcX");
        batch.Count.Should().Be(0);
    }

    [Fact]
    public void Flush_OffscreenSprite_IsSkipped()
    {
        WorldSpriteBatch batch = new(2);
        Camera2D camera = default;
        camera.Follow(50, 50, 100, 100, 1000, 1000);
        RenderCommandBuffer buffer = new(2);
        buffer.Begin(1);
        batch.Begin();
        batch.Add(1, 0, 0, 4, 4, 500, 500, 504);
        batch.Add(2, 0, 0, 4, 4, 10, 10, 14);

        batch.Flush(buffer, camera);

        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void Flush_SteadyState_DoesNotAllocate()
    {
        WorldSpriteBatch batch = new(64);
        Camera2D camera = default;
        camera.Follow(50, 50, 100, 100, 100, 100);
        RenderCommandBuffer buffer = new(64);
        for (int warm = 0; warm < 3; warm++)
        {
            Frame();
        }

        // The runtime may allocate once on this thread while it promotes the batch's methods to optimized code (tiered JIT, a loaded machine makes it land inside a
        // window). A leak allocates in every window, so one window of 100 frames with no allocation shows the steady state.
        long smallest = long.MaxValue;
        for (int window = 0; window < 10 && smallest != 0; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Frame();
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        smallest.Should().Be(0);

        void Frame()
        {
            buffer.Begin(1);
            batch.Begin();
            for (int n = 0; n < 40; n++)
            {
                batch.Add(1, 0, 0, 4, 4, n, n, (n * 7) % 13, n);
            }

            batch.Flush(buffer, camera);
        }
    }

    [Fact]
    public void PropLayer_Farmhouse_SplitsBaseAndRoofAroundFootprintAnchor()
    {
        PropDefinition house = new("house", "sheet", 16, 0, 48, 64, 3, 2, FrontHeight: 16);
        PropLayer layer = new([new PropPlacement(house, 2, 4)], _ => 9, 16);
        Camera2D camera = default;
        camera.Follow(50, 50, 200, 200, 200, 200);
        WorldSpriteBatch batch = new(4);
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(1);
        batch.Begin();

        layer.AddToBatch(batch);
        batch.Flush(buffer, camera);
        layer.DrawFront(buffer, camera);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        // Footprint bottom = (4 + 2) * 16 = 96, image top = 96 - 64 = 32, left = 32.
        ints[2..9].Should().Equal(9, 16, 16, 48, 48, 32, 48); // base: below the roof
        ints[11..18].Should().Equal(9, 16, 0, 48, 16, 32, 32); // roof: top 16 rows
    }

    [Fact]
    public void TileLayerRenderer_Draw_WaterWave_RedrawsShiftedStripsOnlyOnWaterTiles()
    {
        Dictionary<int, KeyValuePair<string, string>[]> props = new() { [0] = [new("Water", "T")] };
        TileSet set = new(1, "t", "t", 2, 4, new TileProperties(4, props, new Dictionary<int, TileAnimation>()));
        TileMap map = new(2, 1, 16, 16, [set], [new TileLayer("Back", [1, 2])]);
        Camera2D camera = default;
        camera.Follow(16, 8, 32, 16, 32, 16);
        TileWave wave = new(TileTraits.Water, 4, 1, 16, 1000);
        RenderCommandBuffer plain = new(64);
        RenderCommandBuffer rippled = new(64);
        plain.Begin(1);
        rippled.Begin(1);

        TileLayerRenderer.Draw(map, map.Layers[0], [0], camera, plain, 0, wave: default);
        TileLayerRenderer.Draw(map, map.Layers[0], [0], camera, rippled, 0, wave);

        plain.Count.Should().Be(2);
        rippled.Count.Should().BeGreaterThan(2).And.BeLessThanOrEqualTo(2 + 4);
        wave.Offset(0, 0).Should().Be(0);
        wave.Offset(4, 0).Should().Be(1);
        wave.Offset(4, 500).Should().Be(-1);
    }

    [Fact]
    public void FillBlack_Opacity_RecordsAFillCommandWithTheOpacityAsTint()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(2);

        buffer.FillBlack(300, 320, 180);
        buffer.FillBlack(0, 320, 180);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        buffer.Count.Should().Be(1, "a clear fade records nothing");
        ints[2..11].Should().Equal(RenderCommandBuffer.BlackFill, 0, 0, 320, 180, 0, 0, 0, 255);
    }

    [Fact]
    public void TileLayerPlan_For_SplitsGroundFrontAndSkipsLogicLayers()
    {
        string[] names = ["Back", "Buildings", "Paths", "Buildings2", "Front", "AlwaysFront", "AlwaysFront2", "AlwaysFront3"];
        TileMap map = new(1, 1, 16, 16, [new TileSet(1, "t", "t", 1, 1)], [.. names.Select(n => new TileLayer(n, [0]))]);

        TileLayerPlan plan = TileLayerPlan.For(map);

        plan.Ground.ToArray().Select(l => l.Name).Should().Equal("Back", "Buildings", "Buildings2");
        plan.Front.ToArray().Select(l => l.Name).Should().Equal("Front", "AlwaysFront", "AlwaysFront2", "AlwaysFront3");
    }

    [Fact]
    public void DrawLabel_RecordsOneLabelCommandWithTheIdAndPosition_AndIgnoresZero()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(2);

        buffer.DrawLabel(7, 120, 80);
        buffer.DrawLabel(0, 1, 1);

        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        ints.Should().Equal(1, 2, RenderCommandBuffer.LabelTexture, 7, 0, 0, 0, 120, 80, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void LabelRegistry_UploadsEachTextOnce_AndLooksUpWithoutUploading()
    {
        List<(int Id, string Text, int Style)> uploads = [];
        LabelRegistry registry = new((id, text, style) => uploads.Add((id, text, style)));

        int a = registry.Intern("Nguyễn Văn An", 0);
        int again = registry.Intern("Nguyễn Văn An", 0);
        int own = registry.Intern("Nguyễn Văn An", 1);

        again.Should().Be(a);
        own.Should().NotBe(a, "the local player's style is a separate bitmap");
        uploads.Should().HaveCount(2);
        registry.TryGet("Nguyễn Văn An", 0, out int found).Should().BeTrue();
        found.Should().Be(a);
        registry.TryGet("unknown", 0, out _).Should().BeFalse();
        uploads.Should().HaveCount(2, "TryGet never uploads");
        registry.Intern(string.Empty, 0).Should().Be(0);
    }

    [Fact]
    public void LabelRegistry_PastCapacity_DrawsNothingMore()
    {
        LabelRegistry registry = new((_, _, _) => { }, capacity: 2);

        registry.Intern("one", 0).Should().BeGreaterThan(0);
        registry.Intern("two", 0).Should().BeGreaterThan(0);
        registry.Intern("three", 0).Should().Be(0);
    }
}
