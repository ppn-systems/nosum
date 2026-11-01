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

/// <summary>Tests the baked tile chunks (docs/adr/ADR-086): what a frame records for a tile layer, bake budget and reuse.</summary>
public sealed class TileChunkCacheTests
{
    private const int Tile = 16;

    // The texture of the tile set in these tests (Frame passes it as the only tile set).
    private const int TileTexture = 0;

    [Fact]
    public void TileLayerRenderer_DrawTiles_StaticAndLive_TogetherDrawEveryTile()
    {
        // Tile 0 is animated, tile 1 is plain; the layer has one of each per row.
        TileAnimation flicker = new([0, 1], [100, 100]);
        TileSet set = new(1, "t", "t", 2, 4, new TileProperties(4, new Dictionary<int, KeyValuePair<string, string>[]>(), new Dictionary<int, TileAnimation> { [0] = flicker }));
        TileMap map = new(4, 4, Tile, Tile, [set], [new TileLayer("Back", [1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2])]);
        TileLayer layer = map.Layers[0];

        RenderCommandBuffer all = Record(c => TileLayerRenderer.DrawTiles(map, layer, [9], 0, 0, 3, 3, 0, 0, c, 0, default, RenderCommandBuffer.NoTint, TileLayerPart.All));
        RenderCommandBuffer stat = Record(c => TileLayerRenderer.DrawTiles(map, layer, [9], 0, 0, 3, 3, 0, 0, c, 0, default, RenderCommandBuffer.NoTint, TileLayerPart.Static));
        RenderCommandBuffer live = Record(c => TileLayerRenderer.DrawTiles(map, layer, [9], 0, 0, 3, 3, 0, 0, c, 0, default, RenderCommandBuffer.NoTint, TileLayerPart.Live));

        all.Count.Should().Be(16);
        (stat.Count + live.Count).Should().Be(all.Count);
        live.Count.Should().Be(8, "every second tile is animated, so it is live");
        stat.Count.Should().Be(8);
    }

    [Fact]
    public void TileChunkCache_DrawLayer_FirstFrame_BakesTheChunkThenBlitsIt()
    {
        TileMap map = FlatMap(16, 16);
        TileChunkCache cache = new([7]);
        Camera2D camera = Camera(map, 0, 0, 256, 256);

        RenderCommandBuffer frame = Frame(cache, map, camera);

        List<int[]> commands = Commands(frame);
        commands[0].Take(3).Should().Equal(RenderCommandBuffer.BeginTargetTexture, 7, 0);
        commands[0][3..5].Should().Equal(256, 256);
        commands.Count(c => c[0] == TileTexture).Should().Be(256, "all 256 tiles are baked, one draw each");
        commands[257][0].Should().Be(RenderCommandBuffer.EndTargetTexture);
        commands[258].Take(9).Should().Equal(7, 0, 0, 256, 256, 0, 0, 0, RenderCommandBuffer.NoTint);
        commands.Should().HaveCount(259);
    }

    [Fact]
    public void TileChunkCache_DrawLayer_LaterFrame_ReusesTheBakedChunk()
    {
        TileMap map = FlatMap(16, 16);
        TileChunkCache cache = new([7]);
        Camera2D camera = Camera(map, 0, 0, 256, 256);
        Frame(cache, map, camera);

        RenderCommandBuffer second = Frame(cache, map, camera);

        List<int[]> commands = Commands(second);
        commands.Should().HaveCount(1, "nothing is baked again: one blit only");
        commands[0].Take(9).Should().Equal(7, 0, 0, 256, 256, 0, 0, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void TileChunkCache_DrawLayer_CameraInsideChunk_BlitsWholeTilesAroundTheCamera()
    {
        TileMap map = FlatMap(32, 32);
        TileChunkCache cache = new([7, 8, 9, 10]);
        Camera2D camera = Camera(map, 40, 24, 64, 48);

        RenderCommandBuffer frame = Frame(cache, map, camera);

        // Tiles columns 2..6 and rows 1..4 are on screen, all in chunk (0, 0). The blit takes those whole tiles, so the part past the
        // camera's left and top edges is cut by the view, exactly as the tiles would be.
        List<int[]> blits = Commands(frame).Where(c => c[0] is >= 7 and <= 10).ToList();
        blits.Should().HaveCount(1);
        blits[0].Take(9).Should().Equal(7, 32, 16, 80, 64, -8, -8, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void TileChunkCache_DrawLayer_MoreChunksThanBakesPerFrame_DrawsTheRestTileByTile()
    {
        // Five chunks side by side; a frame bakes four and draws the fifth tile by tile, so no target is opened for it.
        TileMap map = FlatMap(80, 16);
        TileChunkCache cache = new([7, 8, 9, 10, 11]);
        Camera2D camera = Camera(map, 0, 0, 1280, 256);

        RenderCommandBuffer frame = Frame(cache, map, camera);

        List<int[]> commands = Commands(frame);
        commands.Count(c => c[0] == RenderCommandBuffer.BeginTargetTexture).Should().Be(TileChunkCache.MaxBakesPerFrame);
        commands.Count(c => c[0] == TileTexture).Should().Be((TileChunkCache.MaxBakesPerFrame + 1) * 256, "four baked chunks, and the fifth drawn directly");
    }

    [Fact]
    public void TileChunkCache_DrawLayer_AnimatedTile_IsDrawnEveryFrameOverTheChunk()
    {
        // Gid 1 is the animated tile (its local frame 0 flips to frame 1 every 100 ms); the rest are plain tiles of gid 2.
        TileAnimation flicker = new([0, 1], [100, 100]);
        TileSet set = new(1, "t", "t", 2, 4, new TileProperties(4, new Dictionary<int, KeyValuePair<string, string>[]>(), new Dictionary<int, TileAnimation> { [0] = flicker }));
        int[] gids = Enumerable.Repeat(2, 256).ToArray();
        gids[0] = 1;
        TileMap map = new(16, 16, Tile, Tile, [set], [new TileLayer("Back", gids)]);
        TileChunkCache cache = new([7]);
        Camera2D camera = Camera(map, 0, 0, 256, 256);

        RenderCommandBuffer first = Frame(cache, map, camera, timeMs: 0);
        RenderCommandBuffer second = Frame(cache, map, camera, timeMs: 100);

        // The first frame bakes 255 plain tiles and draws the animated one live; the second copies the chunk and draws only the animated tile, on its next frame.
        List<int[]> firstCommands = Commands(first);
        firstCommands.Count(c => c[0] == TileTexture).Should().Be(256);
        List<int[]> secondCommands = Commands(second);
        secondCommands.Should().HaveCount(2);
        secondCommands[0][0].Should().Be(7, "the chunk is copied");
        secondCommands[1].Take(3).Should().Equal(TileTexture, 16, 0);
    }

    [Fact]
    public void TileChunkCache_DrawLayer_NewEpoch_RebakesTheChunk()
    {
        TileMap map = FlatMap(16, 16);
        TileChunkCache cache = new([7]);
        Camera2D camera = Camera(map, 0, 0, 256, 256);
        Frame(cache, map, camera, epoch: 1);

        RenderCommandBuffer next = Frame(cache, map, camera, epoch: 2);

        Commands(next).Count(c => c[0] == RenderCommandBuffer.BeginTargetTexture).Should().Be(1, "another map's bitmap is never reused");
    }

    private static TileMap FlatMap(int width, int height)
    {
        int[] gids = new int[width * height];
        Array.Fill(gids, 1);
        return new TileMap(width, height, Tile, Tile, [new TileSet(1, "t", "t", 2, 4)], [new TileLayer("Back", gids)]);
    }

    // A camera whose left and top are the given pixels, clamped as the game clamps it to the map.
    private static Camera2D Camera(TileMap map, int left, int top, int width, int height)
    {
        Camera2D camera = default;
        camera.Follow(left + (width / 2), top + (height / 2), width, height, map.Width * Tile, map.Height * Tile);
        return camera;
    }

    private static RenderCommandBuffer Frame(TileChunkCache cache, TileMap map, Camera2D camera, long timeMs = 0, int epoch = 1)
    {
        RenderCommandBuffer buffer = new(4096);
        buffer.Begin(1);
        cache.BeginFrame();
        cache.DrawLayer(map, map.Layers[0], 0, epoch, [0], camera, buffer, timeMs, default, RenderCommandBuffer.NoTint);
        return buffer;
    }

    private static RenderCommandBuffer Record(Action<RenderCommandBuffer> record)
    {
        RenderCommandBuffer buffer = new(4096);
        buffer.Begin(1);
        record(buffer);
        return buffer;
    }

    private static List<int[]> Commands(RenderCommandBuffer buffer)
    {
        int[] ints = MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();
        List<int[]> commands = new(buffer.Count);
        for (int i = 0; i < buffer.Count; i++)
        {
            commands.Add(ints[(2 + (i * RenderCommandBuffer.IntsPerCommand))..(2 + ((i + 1) * RenderCommandBuffer.IntsPerCommand))]);
        }

        return commands;
    }
}
