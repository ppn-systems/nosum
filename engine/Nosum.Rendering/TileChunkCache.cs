// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>
/// Bakes the static tiles of a tile layer into 16 × 16 tile chunk bitmaps and draws each visible chunk as one image
/// (docs/adr/ADR-086). A static tile does not change between frames, so the browser copies a chunk instead of the
/// hundreds of tiles in it; animated tiles and water ripples are still drawn every frame, over the chunk.
/// </summary>
/// <remarks>
/// The bitmaps are render targets of the backend: the pool holds backend texture ids, and the backend creates each bitmap the first time
/// a bake opens it. A chunk is baked at most <see cref="MaxBakesPerFrame"/> times a frame; a chunk that waits for its turn is drawn tile by tile
/// that frame, so the picture is the same and a map change never spends one long frame on baking. Nothing is allocated per frame.
/// </remarks>
public sealed class TileChunkCache
{
    /// <summary>Tiles along one side of a chunk.</summary>
    public const int ChunkTiles = 16;

    /// <summary>Most chunks baked in one frame.</summary>
    public const int MaxBakesPerFrame = 4;

    // Worst case per tile: the tile and four water strips. A bake needs room for a whole chunk and the end command, or it is not started.
    private const int BakeRoom = (ChunkTiles * ChunkTiles * 5) + 2;

    private readonly int[] _targets;
    private readonly Slot[] _slots;
    private int _frame;
    private int _bakes;

    /// <summary>Initializes the cache with a pool of render-target texture ids; one bitmap per id.</summary>
    /// <param name="targetIds">Backend texture ids reserved for the chunks, at least one.</param>
    public TileChunkCache(int[] targetIds)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        ArgumentOutOfRangeException.ThrowIfZero(targetIds.Length);
        _targets = (int[])targetIds.Clone();
        _slots = new Slot[targetIds.Length];
    }

    /// <summary>Starts a frame: the bake budget is counted again.</summary>
    public void BeginFrame()
    {
        _frame++;
        _bakes = 0;
    }

    /// <summary>
    /// Records one tile layer over the camera: visible chunks are blitted from their bitmaps (baked now when they are not yet and the frame has budget),
    /// then the layer's live tiles are drawn over them; a chunk that is not ready is drawn tile by tile.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="layer">The layer.</param>
    /// <param name="layerKey">Which layer of the scene this is, below 256 (the ground and front layers of the map get different keys).</param>
    /// <param name="epoch">Changes whenever the map content is replaced, so a bitmap is never reused for another map.</param>
    /// <param name="tileSetTextures">Backend texture id per entry of <see cref="TileMap.TileSets"/>.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="timeMs">Animation clock, milliseconds.</param>
    /// <param name="wave">Water ripple, or <see cref="TileWave.None"/>.</param>
    /// <param name="tint">Multiply tint, or <see cref="RenderCommandBuffer.NoTint"/>.</param>
    public void DrawLayer(TileMap map, TileLayer layer, int layerKey, int epoch, ReadOnlySpan<int> tileSetTextures, in Camera2D camera, RenderCommandBuffer commands, long timeMs, TileWave wave, int tint)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(commands);

        int tw = map.TileWidth;
        int th = map.TileHeight;
        int x0 = Math.Max(0, Floor(camera.Left, tw));
        int y0 = Math.Max(0, Floor(camera.Top, th));
        int x1 = Math.Min(map.Width - 1, Floor(camera.Left + camera.Width - 1, tw));
        int y1 = Math.Min(map.Height - 1, Floor(camera.Top + camera.Height - 1, th));
        if (x1 < x0 || y1 < y0)
        {
            return;
        }

        for (int cy = y0 / ChunkTiles; cy <= y1 / ChunkTiles; cy++)
        {
            for (int cx = x0 / ChunkTiles; cx <= x1 / ChunkTiles; cx++)
            {
                // The part of this chunk that is on screen, in map tiles.
                int tx0 = Math.Max(x0, cx * ChunkTiles);
                int ty0 = Math.Max(y0, cy * ChunkTiles);
                int tx1 = Math.Min(x1, (cx * ChunkTiles) + ChunkTiles - 1);
                int ty1 = Math.Min(y1, (cy * ChunkTiles) + ChunkTiles - 1);

                int slot = this.ReadySlot(map, layer, layerKey, epoch, cx, cy, tint, tileSetTextures, wave, commands);
                if (slot < 0)
                {
                    TileLayerRenderer.DrawTiles(map, layer, tileSetTextures, tx0, ty0, tx1, ty1, camera.Left, camera.Top, commands, timeMs, wave, tint, TileLayerPart.All);
                    continue;
                }

                // Blit: the chunk bitmap's pixels for this rectangle, at their place on screen.
                int chunkLeft = cx * ChunkTiles * tw;
                int chunkTop = cy * ChunkTiles * th;
                commands.Draw(
                    _targets[slot],
                    (tx0 * tw) - chunkLeft,
                    (ty0 * th) - chunkTop,
                    (tx1 - tx0 + 1) * tw,
                    (ty1 - ty0 + 1) * th,
                    (tx0 * tw) - camera.Left,
                    (ty0 * th) - camera.Top,
                    0,
                    RenderCommandBuffer.NoTint);
                TileLayerRenderer.DrawTiles(map, layer, tileSetTextures, tx0, ty0, tx1, ty1, camera.Left, camera.Top, commands, timeMs, wave, tint, TileLayerPart.Live);
            }
        }
    }

    // The slot whose bitmap holds this chunk, baked if needed; -1 when it must be drawn tile by tile this frame.
    private int ReadySlot(TileMap map, TileLayer layer, int layerKey, int epoch, int cx, int cy, int tint, ReadOnlySpan<int> tileSetTextures, TileWave wave, RenderCommandBuffer commands)
    {
        int found = -1;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Matches(epoch, layerKey, cx, cy, tint))
            {
                found = i;
                break;
            }
        }

        if (found < 0)
        {
            found = this.Claim(epoch, layerKey, cx, cy, tint);
            if (found < 0)
            {
                return -1;
            }
        }

        _slots[found].LastUsed = _frame;
        if (_slots[found].Baked)
        {
            return found;
        }

        if (_bakes >= MaxBakesPerFrame || commands.Capacity - commands.Count < BakeRoom)
        {
            return -1;
        }

        _bakes++;
        int left = cx * ChunkTiles;
        int top = cy * ChunkTiles;
        int right = Math.Min(map.Width - 1, left + ChunkTiles - 1);
        int bottom = Math.Min(map.Height - 1, top + ChunkTiles - 1);
        commands.BeginTarget(_targets[found], ChunkTiles * map.TileWidth, ChunkTiles * map.TileHeight);
        TileLayerRenderer.DrawTiles(map, layer, tileSetTextures, left, top, right, bottom, left * map.TileWidth, top * map.TileHeight, commands, 0, wave, tint, TileLayerPart.Static);
        commands.EndTarget();
        _slots[found].Baked = true;
        return found;
    }

    // Takes the slot not used this frame that was used longest ago (an unused one first); -1 when every slot is in use this frame.
    private int Claim(int epoch, int layerKey, int cx, int cy, int tint)
    {
        int best = -1;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].LastUsed == _frame)
            {
                continue;
            }

            if (best < 0 || _slots[i].LastUsed < _slots[best].LastUsed)
            {
                best = i;
            }
        }

        if (best >= 0)
        {
            _slots[best] = new Slot { Epoch = epoch, LayerKey = layerKey, CellX = cx, CellY = cy, Tint = tint, Valid = true, Baked = false, LastUsed = 0 };
        }

        return best;
    }

    private static int Floor(int value, int size) => value >= 0 ? value / size : ((value + 1) / size) - 1;

    private struct Slot
    {
        public int Epoch;
        public int LayerKey;
        public int CellX;
        public int CellY;
        public int Tint;
        public bool Valid;
        public bool Baked;
        public int LastUsed;

        public readonly bool Matches(int epoch, int layerKey, int cx, int cy, int tint) =>
            Valid && Epoch == epoch && LayerKey == layerKey && CellX == cx && CellY == cy && Tint == tint;
    }
}
