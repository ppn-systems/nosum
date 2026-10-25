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
    /// <summary>Kinds the own tileset has fill tiles for, lowest priority first. A higher kind is drawn over a lower one with rounded edges.</summary>
    public static readonly TerrainKind[] Fills = [TerrainKind.Water, TerrainKind.Path, TerrainKind.Cliff, TerrainKind.Sand, TerrainKind.Dirt, TerrainKind.Grass];

    /// <summary>Kinds with a set of rounded edge pieces (the rest have square edges), lowest priority first.</summary>
    public static readonly TerrainKind[] Rounded = [TerrainKind.Cliff, TerrainKind.Sand, TerrainKind.Dirt, TerrainKind.Grass];

    /// <summary>Returns the draw priority of a kind: a higher one overlaps a lower one. <see cref="TerrainKind.Other"/> has none.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The priority, 0 for water up to 5 for grass; -1 for <see cref="TerrainKind.Other"/>.</returns>
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
