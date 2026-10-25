// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
