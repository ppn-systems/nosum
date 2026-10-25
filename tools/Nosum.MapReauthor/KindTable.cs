// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Nosum.MapReauthor;

/// <summary>
/// The mapping from (tilesheet, local tile index) to a <see cref="TerrainKind"/>, committed as <c>terrain-kinds.csv</c> and grown per map.
/// A tile that is not in the table is unclassified: it stays on its placeholder sheet and the report lists it.
/// </summary>
internal sealed class KindTable
{
    private readonly Dictionary<(string Sheet, int Index), TerrainKind> _kinds;

    private KindTable(Dictionary<(string Sheet, int Index), TerrainKind> kinds) => _kinds = kinds;

    /// <summary>Gets the number of tiles in the table.</summary>
    public int Count => _kinds.Count;

    /// <summary>Reads the table committed with the tool.</summary>
    /// <returns>The table.</returns>
    public static KindTable Embedded()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("terrain-kinds.csv")
            ?? throw new InvalidOperationException("terrain-kinds.csv is not embedded.");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the CSV text: <c>sheet,index,kind</c> lines; lines starting with <c>#</c> and the header are skipped.</summary>
    /// <param name="csv">The table text.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FormatException">A line is malformed or a tile is listed twice.</exception>
    public static KindTable Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        Dictionary<(string, int), TerrainKind> kinds = [];
        foreach (string raw in csv.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("sheet,", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                throw new FormatException($"Bad kind table line '{line}'.");
            }

            if (!kinds.TryAdd((parts[0], index), TerrainKinds.Parse(parts[2])))
            {
                throw new FormatException($"Tile {parts[0]}:{index} is listed twice.");
            }
        }

        return new KindTable(kinds);
    }

    /// <summary>Looks up a tile.</summary>
    /// <param name="sheet">The tileset image name without extension, as the map names it.</param>
    /// <param name="index">The local tile index.</param>
    /// <param name="kind">The kind, when the tile is listed.</param>
    /// <returns><see langword="true"/> when the tile is in the table (a tile listed as <see cref="TerrainKind.Other"/> is deliberately kept).</returns>
    public bool TryGet(string sheet, int index, out TerrainKind kind) => _kinds.TryGetValue((sheet, index), out kind);
}
