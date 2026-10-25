// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Core;

/// <summary>One tileset referenced by a map: a sheet image cut into equal tiles.</summary>
/// <param name="FirstGid">Global id of the sheet's first tile in the map.</param>
/// <param name="Name">Tileset name.</param>
/// <param name="ImageSource">Image file name as written in the map (no folder; may lack an extension).</param>
/// <param name="Columns">Tiles per sheet row.</param>
/// <param name="TileCount">Number of tiles in the sheet.</param>
public sealed record TileSet(int FirstGid, string Name, string ImageSource, int Columns, int TileCount);

/// <summary>One tile layer: a row-major grid of global tile ids, 0 meaning empty.</summary>
/// <param name="Name">Layer name (e.g. <c>Back</c>, <c>Buildings</c>, <c>Front</c>).</param>
/// <param name="Gids">Global tile ids, <c>width × height</c> entries, row-major.</param>
public sealed record TileLayer(string Name, int[] Gids);

/// <summary>
/// An orthogonal tile map: size, tile size, tilesets and tile layers in draw order.
/// </summary>
public sealed class TileMap
{
    /// <summary>Initializes a map.</summary>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="tileWidth">Tile width in pixels.</param>
    /// <param name="tileHeight">Tile height in pixels.</param>
    /// <param name="tileSets">Tilesets ordered by <see cref="TileSet.FirstGid"/>.</param>
    /// <param name="layers">Tile layers in draw order.</param>
    public TileMap(int width, int height, int tileWidth, int tileHeight, IReadOnlyList<TileSet> tileSets, IReadOnlyList<TileLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(tileSets);
        ArgumentNullException.ThrowIfNull(layers);

        this.Width = width;
        this.Height = height;
        this.TileWidth = tileWidth;
        this.TileHeight = tileHeight;
        this.TileSets = tileSets;
        this.Layers = layers;
    }

    /// <summary>Gets the width in tiles.</summary>
    public int Width { get; }

    /// <summary>Gets the height in tiles.</summary>
    public int Height { get; }

    /// <summary>Gets the tile width in pixels.</summary>
    public int TileWidth { get; }

    /// <summary>Gets the tile height in pixels.</summary>
    public int TileHeight { get; }

    /// <summary>Gets the tilesets ordered by first global id.</summary>
    public IReadOnlyList<TileSet> TileSets { get; }

    /// <summary>Gets the tile layers in draw order.</summary>
    public IReadOnlyList<TileLayer> Layers { get; }

    /// <summary>Returns the layer with the given name, or <see langword="null"/>.</summary>
    /// <param name="name">Layer name, compared ordinally.</param>
    /// <returns>The layer or <see langword="null"/>.</returns>
    public TileLayer? FindLayer(string name)
    {
        foreach (TileLayer layer in this.Layers)
        {
            if (string.Equals(layer.Name, name, StringComparison.Ordinal))
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the tileset holding a global tile id.
    /// </summary>
    /// <param name="gid">Global tile id (greater than 0).</param>
    /// <returns>The index into <see cref="TileSets"/>, or -1 when no tileset holds it.</returns>
    public int FindTileSet(int gid)
    {
        for (int i = this.TileSets.Count - 1; i >= 0; i--)
        {
            TileSet set = this.TileSets[i];
            if (gid >= set.FirstGid)
            {
                return gid < set.FirstGid + set.TileCount ? i : -1;
            }
        }

        return -1;
    }
}
