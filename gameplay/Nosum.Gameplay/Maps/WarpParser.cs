// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nosum.Gameplay.Maps;

/// <summary>Reads the <c>Warp</c> map property of a Tiled map.</summary>
public static class WarpParser
{
    /// <summary>Name of the map property that lists a map's exits.</summary>
    public const string PropertyName = "Warp";

    private const int FieldsPerWarp = 5;

    /// <summary>
    /// Parses space-separated groups of five values: <c>fromX fromY targetMap targetX targetY</c>.
    /// The target stays as written; <see cref="GameMaps"/> resolves it to a map id.
    /// </summary>
    /// <param name="value">The property value; <see langword="null"/> or blank means no exits.</param>
    /// <returns>The exits in file order.</returns>
    /// <exception cref="FormatException">The value is not a whole number of groups, or a coordinate is not an integer.</exception>
    public static List<MapWarp> Parse(string? value)
    {
        List<MapWarp> warps = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return warps;
        }

        string[] fields = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length % FieldsPerWarp != 0)
        {
            throw new FormatException($"Warp property has {fields.Length} values; expected groups of {FieldsPerWarp}.");
        }

        for (int i = 0; i < fields.Length; i += FieldsPerWarp)
        {
            warps.Add(new MapWarp(Int(fields[i]), Int(fields[i + 1]), fields[i + 2], Int(fields[i + 3]), Int(fields[i + 4])));
        }

        return warps;
    }

    private static int Int(string field)
        => int.TryParse(field, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"Warp coordinate '{field}' is not an integer.");
}
