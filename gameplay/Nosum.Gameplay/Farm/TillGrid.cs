// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Farm;

/// <summary>Which tiles of a map a hoe can till: diggable ground that nothing blocks.</summary>
public sealed class TillGrid
{
    private readonly bool[] _tillable;

    /// <summary>Initializes a grid.</summary>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="tillable">Row-major flags, <see langword="true"/> for tillable tiles.</param>
    public TillGrid(int width, int height, bool[] tillable)
    {
        ArgumentNullException.ThrowIfNull(tillable);
        ArgumentOutOfRangeException.ThrowIfNotEqual(tillable.Length, width * height);

        this.Width = width;
        this.Height = height;
        _tillable = tillable;
    }

    /// <summary>Gets the width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the height in tiles.</summary>
    public int Height { get; }

    /// <summary>
    /// Builds the grid from a map: a tile is tillable when its ground-layer tile has the <c>Diggable</c>
    /// property and the walk grid does not block it (buildings, trees, rocks, water, props).
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="walk">The map's walk grid; client and server build the same one.</param>
    /// <param name="groundLayer">Name of the terrain layer that carries <c>Diggable</c>.</param>
    /// <returns>The grid.</returns>
    public static TillGrid FromMap(TileMap map, WalkGrid walk, string groundLayer = "Back")
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(walk);

        bool[] tillable = new bool[map.Width * map.Height];
        if (map.FindLayer(groundLayer) is TileLayer ground)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int i = (y * map.Width) + x;
                    tillable[i] = (map.GetTraits(ground.Gids[i]) & TileTraits.Diggable) != 0 && !walk.IsBlocked(x, y);
                }
            }
        }

        return new TillGrid(map.Width, map.Height, tillable);
    }

    /// <summary>Returns whether a tile can be tilled; tiles outside the map cannot.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when a hoe works there.</returns>
    public bool IsTillable(int x, int y)
        => x >= 0 && y >= 0 && x < this.Width && y < this.Height && _tillable[(y * this.Width) + x];

    /// <summary>Returns whether a tile is inside the map.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when the tile exists.</returns>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < this.Width && y < this.Height;
}
