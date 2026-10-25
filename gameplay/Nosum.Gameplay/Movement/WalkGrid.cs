// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// Which tiles of a map can be walked on.
/// </summary>
public sealed class WalkGrid
{
    private readonly bool[] _blocked;

    /// <summary>Initializes a grid.</summary>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="tileSize">Tile size in pixels.</param>
    /// <param name="blocked">Row-major flags, <see langword="true"/> for a tile that cannot be entered.</param>
    public WalkGrid(int width, int height, int tileSize, bool[] blocked)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        ArgumentOutOfRangeException.ThrowIfNotEqual(blocked.Length, width * height);

        this.Width = width;
        this.Height = height;
        this.TileSize = tileSize;
        _blocked = blocked;
    }

    /// <summary>Returns an independent copy, to which a per-instance layer (placed furniture) can add blocked tiles without touching the map's shared grid.</summary>
    /// <returns>The copy.</returns>
    public WalkGrid Clone() => new(this.Width, this.Height, this.TileSize, (bool[])_blocked.Clone());

    /// <summary>Gets the width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the height in tiles.</summary>
    public int Height { get; }

    /// <summary>Gets the tile size in pixels.</summary>
    public int TileSize { get; }

    /// <summary>
    /// Builds a grid where every non-empty tile of the given layer blocks (the map's
    /// <c>Buildings</c> layer: walls, fences, trees, rocks).
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="blockingLayer">Name of the layer whose tiles block.</param>
    /// <returns>The grid; with no such layer, everything is walkable.</returns>
    public static WalkGrid FromLayer(TileMap map, string blockingLayer)
    {
        ArgumentNullException.ThrowIfNull(map);

        bool[] blocked = new bool[map.Width * map.Height];
        if (map.FindLayer(blockingLayer) is TileLayer layer)
        {
            for (int i = 0; i < blocked.Length; i++)
            {
                blocked[i] = layer.Gids[i] != 0;
            }
        }

        return new WalkGrid(map.Width, map.Height, map.TileWidth, blocked);
    }

    /// <summary>
    /// Builds a grid from a map's tile properties: a non-empty <paramref name="blockingLayer"/> tile
    /// blocks unless its <c>Passable</c> property is true; a <paramref name="groundLayer"/> tile blocks
    /// when it is water or has <c>Passable</c> false. Rules live here so the server can reuse them.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="groundLayer">Name of the terrain layer (water, impassable ground).</param>
    /// <param name="blockingLayer">Name of the layer of walls, fences, trees and rocks.</param>
    /// <returns>The grid; a missing layer contributes nothing.</returns>
    public static WalkGrid FromMap(TileMap map, string groundLayer = "Back", string blockingLayer = "Buildings")
    {
        ArgumentNullException.ThrowIfNull(map);

        bool[] blocked = new bool[map.Width * map.Height];
        if (map.FindLayer(groundLayer) is TileLayer ground)
        {
            for (int i = 0; i < blocked.Length; i++)
            {
                blocked[i] = (map.GetTraits(ground.Gids[i]) & (TileTraits.Water | TileTraits.Impassable)) != 0;
            }
        }

        if (map.FindLayer(blockingLayer) is TileLayer blocking)
        {
            for (int i = 0; i < blocked.Length; i++)
            {
                int gid = blocking.Gids[i];
                if (gid != 0 && (map.GetTraits(gid) & TileTraits.Passable) == 0)
                {
                    blocked[i] = true;
                }
            }
        }

        return new WalkGrid(map.Width, map.Height, map.TileWidth, blocked);
    }

    /// <summary>Marks a rectangle of tiles as blocked; parts outside the grid are ignored.</summary>
    /// <param name="tileX">Left tile.</param>
    /// <param name="tileY">Top tile.</param>
    /// <param name="tilesWide">Width in tiles.</param>
    /// <param name="tilesHigh">Height in tiles.</param>
    public void Block(int tileX, int tileY, int tilesWide, int tilesHigh)
    {
        for (int y = Math.Max(0, tileY); y < Math.Min(this.Height, tileY + tilesHigh); y++)
        {
            for (int x = Math.Max(0, tileX); x < Math.Min(this.Width, tileX + tilesWide); x++)
            {
                _blocked[(y * this.Width) + x] = true;
            }
        }
    }

    /// <summary>Blocks every tile another grid of the same size blocks, so two layers built on the same map (nodes, placed objects) can be joined.</summary>
    /// <param name="other">A grid of the same width and height.</param>
    public void BlockWhereBlocked(WalkGrid other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentOutOfRangeException.ThrowIfNotEqual(other._blocked.Length, _blocked.Length);

        for (int i = 0; i < _blocked.Length; i++)
        {
            _blocked[i] |= other._blocked[i];
        }
    }

    /// <summary>Marks one tile as walkable; a tile outside the grid is ignored.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    public void Open(int x, int y)
    {
        if (x >= 0 && y >= 0 && x < this.Width && y < this.Height)
        {
            _blocked[(y * this.Width) + x] = false;
        }
    }

    /// <summary>Blocks the collision footprint of every prop (docs/adr/ADR-005 D6).</summary>
    /// <param name="props">Placed buildings and props.</param>
    public void BlockFootprints(IReadOnlyList<PropPlacement> props)
    {
        ArgumentNullException.ThrowIfNull(props);

        foreach (PropPlacement prop in props)
        {
            this.Block(prop.TileX, prop.TileY, prop.Definition.FootprintWidth, prop.Definition.FootprintHeight);
        }
    }

    /// <summary>Returns whether a tile blocks; tiles outside the map block.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when the tile cannot be entered.</returns>
    public bool IsBlocked(int x, int y)
        => x < 0 || y < 0 || x >= this.Width || y >= this.Height || _blocked[(y * this.Width) + x];

    /// <summary>
    /// Returns whether an axis-aligned box in world pixels overlaps any blocking tile.
    /// </summary>
    /// <param name="left">Left edge.</param>
    /// <param name="top">Top edge.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <returns><see langword="true"/> on overlap.</returns>
    public bool Overlaps(double left, double top, double width, double height)
    {
        int x0 = (int)Math.Floor(left / this.TileSize);
        int y0 = (int)Math.Floor(top / this.TileSize);
        int x1 = (int)Math.Floor((left + width - 0.001) / this.TileSize);
        int y1 = (int)Math.Floor((top + height - 0.001) / this.TileSize);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (this.IsBlocked(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
