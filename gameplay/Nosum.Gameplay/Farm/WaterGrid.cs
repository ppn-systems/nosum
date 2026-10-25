// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Gameplay.Farm;

/// <summary>Which tiles of a map are water, where a watering can is refilled (docs/adr/ADR-021 D2).</summary>
public sealed class WaterGrid
{
    private readonly bool[] _water;

    /// <summary>Initializes a grid.</summary>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="water">Row-major flags, <see langword="true"/> for a water tile.</param>
    public WaterGrid(int width, int height, bool[] water)
    {
        ArgumentNullException.ThrowIfNull(water);
        ArgumentOutOfRangeException.ThrowIfNotEqual(water.Length, width * height);

        this.Width = width;
        this.Height = height;
        _water = water;
    }

    /// <summary>Gets the width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the height in tiles.</summary>
    public int Height { get; }

    /// <summary>Builds the grid from a map: a tile is water when its ground-layer tile has the <c>Water</c> property.</summary>
    /// <param name="map">The map.</param>
    /// <param name="groundLayer">Name of the terrain layer, the one the walk grid reads.</param>
    /// <returns>The grid.</returns>
    public static WaterGrid FromMap(TileMap map, string groundLayer = "Back")
    {
        ArgumentNullException.ThrowIfNull(map);

        bool[] water = new bool[map.Width * map.Height];
        if (map.FindLayer(groundLayer) is TileLayer ground)
        {
            for (int i = 0; i < water.Length; i++)
            {
                water[i] = (map.GetTraits(ground.Gids[i]) & TileTraits.Water) != 0;
            }
        }

        return new WaterGrid(map.Width, map.Height, water);
    }

    /// <summary>Returns whether a tile is water; tiles outside the map are not.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when the tile is water.</returns>
    public bool IsWater(int x, int y) => x >= 0 && y >= 0 && x < this.Width && y < this.Height && _water[(y * this.Width) + x];
}
