// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapReauthor;

/// <summary>What a placeholder tile of a drawn terrain layer is, for choosing the own terrain set that replaces it.</summary>
internal enum TerrainKind
{
    /// <summary>Not terrain, or not classified: the tile stays on its placeholder sheet.</summary>
    Other = 0,

    /// <summary>Grass.</summary>
    Grass,

    /// <summary>Bare earth, tillable or not.</summary>
    Dirt,

    /// <summary>Sand.</summary>
    Sand,

    /// <summary>Water, shallow or deep.</summary>
    Water,

    /// <summary>A made path: cobbles, boards.</summary>
    Path,

    /// <summary>Rock: cliff tops and bare stone ground.</summary>
    Cliff,
}

/// <summary>Order and parsing of <see cref="TerrainKind"/>.</summary>
internal static class TerrainKinds
{
    /// <summary>
    /// Kinds the own tileset has art for, lowest priority first. Where two meet, the higher one wins the corner they share, and a pair that has no
    /// <see cref="WangSet"/> meets through grass. Path and cliff have no own set yet and stay on their placeholder tiles.
    /// </summary>
    public static readonly TerrainKind[] Fills = [TerrainKind.Water, TerrainKind.Sand, TerrainKind.Grass, TerrainKind.Dirt];

    /// <summary>Returns the draw priority of a kind: a higher one overlaps a lower one. <see cref="TerrainKind.Other"/> has none.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The priority, 0 for water up to 3 for dirt; -1 for <see cref="TerrainKind.Other"/>.</returns>
    public static int Priority(TerrainKind kind) => Array.IndexOf(Fills, kind);

    /// <summary>Parses a kind name of the table.</summary>
    /// <param name="text">A lower-case kind name.</param>
    /// <returns>The kind.</returns>
    /// <exception cref="FormatException">The name is not a kind.</exception>
    public static TerrainKind Parse(string text)
        => Enum.TryParse(text, ignoreCase: true, out TerrainKind kind) && Enum.IsDefined(kind)
            ? kind
            : throw new FormatException($"'{text}' is not a terrain kind.");
}
