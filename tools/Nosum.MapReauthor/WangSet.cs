// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapReauthor;

/// <summary>
/// One own corner tileset of two terrains: 16 tiles, one for every combination of its four corners being the lower or the upper terrain
/// (<c>art/own/source/pixellab/&lt;Name&gt;.png</c> and <c>.json</c>). The labels "lower" and "upper" are the file's, and the kinds say what the pixels show.
/// </summary>
/// <param name="Name">The file name without extension.</param>
/// <param name="Lower">The terrain of the corners the file calls lower.</param>
/// <param name="Upper">The terrain of the corners the file calls upper.</param>
/// <param name="Drawn">The set has no file: <see cref="PathArt"/> draws it in code.</param>
internal sealed record WangSet(string Name, TerrainKind Lower, TerrainKind Upper, bool Drawn = false)
{
    /// <summary>The sets, in the order their tiles are laid out in the own tileset. Every pair of kinds not listed here has no direct transition.</summary>
    public static readonly WangSet[] All =
    [
        new("grass_dirt", TerrainKind.Dirt, TerrainKind.Grass),
        new("water_grass", TerrainKind.Water, TerrainKind.Grass),
        new("water_sand", TerrainKind.Water, TerrainKind.Sand),
        new("sand_grass", TerrainKind.Sand, TerrainKind.Grass),
        new("path_grass", TerrainKind.Path, TerrainKind.Grass, Drawn: true),
    ];

    /// <summary>Gets the number of tiles of a set.</summary>
    public const int Tiles = 16;

    /// <summary>Returns the index in <see cref="All"/> of the set that joins two kinds.</summary>
    /// <param name="a">One kind.</param>
    /// <param name="b">The other kind.</param>
    /// <returns>The index, or -1 when no set joins them.</returns>
    public static int Find(TerrainKind a, TerrainKind b)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if ((All[i].Lower == a && All[i].Upper == b) || (All[i].Lower == b && All[i].Upper == a))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the tile code of a corner combination: 1 for the north-west corner being the upper terrain, 2 north-east, 4 south-west, 8 south-east.</summary>
    /// <param name="northWest">The north-west corner.</param>
    /// <param name="northEast">The north-east corner.</param>
    /// <param name="southWest">The south-west corner.</param>
    /// <param name="southEast">The south-east corner.</param>
    /// <returns>The code, 0 to 15.</returns>
    public int Code(TerrainKind northWest, TerrainKind northEast, TerrainKind southWest, TerrainKind southEast)
        => (northWest == this.Upper ? 1 : 0) | (northEast == this.Upper ? 2 : 0) | (southWest == this.Upper ? 4 : 0) | (southEast == this.Upper ? 8 : 0);

    /// <summary>Returns the code the file's metadata gives a tile: its corners as the words lower and upper.</summary>
    /// <param name="northWest">The north-west corner word.</param>
    /// <param name="northEast">The north-east corner word.</param>
    /// <param name="southWest">The south-west corner word.</param>
    /// <param name="southEast">The south-east corner word.</param>
    /// <returns>The code, 0 to 15.</returns>
    public static int CodeOf(string northWest, string northEast, string southWest, string southEast)
        => (IsUpper(northWest) ? 1 : 0) | (IsUpper(northEast) ? 2 : 0) | (IsUpper(southWest) ? 4 : 0) | (IsUpper(southEast) ? 8 : 0);

    private static bool IsUpper(string word) => string.Equals(word, "upper", StringComparison.Ordinal);
}
