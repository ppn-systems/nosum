// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Nosum.Core;

namespace Nosum.MapGen;

/// <summary>
/// One of Nosum's own tilesets as its data file describes it (docs/adr/ADR-042 D3): the sheet's name and image, its grid of square tiles and the
/// traits the rules read on each tile. The file format is documented in <c>tools/README.md</c>. The compiler writes the traits into the
/// <c>.tmx</c> tileset elements in the form <see cref="TmxReader"/> reads (<c>Water</c>, <c>Passable</c>, <c>Diggable</c>).
/// </summary>
internal sealed class TilesetData
{
    /// <summary>Initializes a tileset.</summary>
    /// <param name="name">The tileset name a map and a layout refer to.</param>
    /// <param name="image">The sheet's image file, relative to the folder of the map.</param>
    /// <param name="tileWidth">Tile width in pixels.</param>
    /// <param name="tileHeight">Tile height in pixels.</param>
    /// <param name="columns">Tiles per row of the sheet.</param>
    /// <param name="tileCount">Number of tiles in the sheet.</param>
    /// <param name="traits">The traits of the tiles that have any, by tile index.</param>
    public TilesetData(string name, string image, int tileWidth, int tileHeight, int columns, int tileCount, IReadOnlyDictionary<int, TileTraits> traits)
    {
        this.Name = name;
        this.Image = image;
        this.TileWidth = tileWidth;
        this.TileHeight = tileHeight;
        this.Columns = columns;
        this.TileCount = tileCount;
        this.Traits = traits;
    }

    /// <summary>Gets the tileset name.</summary>
    public string Name { get; }

    /// <summary>Gets the sheet's image file, as the <c>.tmx</c> names it.</summary>
    public string Image { get; }

    /// <summary>Gets the tile width in pixels.</summary>
    public int TileWidth { get; }

    /// <summary>Gets the tile height in pixels.</summary>
    public int TileHeight { get; }

    /// <summary>Gets the tiles per row of the sheet.</summary>
    public int Columns { get; }

    /// <summary>Gets the number of tiles in the sheet.</summary>
    public int TileCount { get; }

    /// <summary>Gets the number of rows of the sheet: the tile count over the columns, rounded up.</summary>
    public int Rows => (this.TileCount + this.Columns - 1) / this.Columns;

    /// <summary>Gets the traits of the tiles that have any, by tile index.</summary>
    public IReadOnlyDictionary<int, TileTraits> Traits { get; }

    /// <summary>Returns whether a name is a valid tileset name: letters, digits and underscores, not empty.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns><see langword="true"/> when the name can name a file.</returns>
    public static bool IsValidName(string name)
        => name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Parses a tileset data file. Lines are read one keyword at a time; blank lines and lines starting with <c>//</c> are ignored.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <param name="source">The file name, for error messages.</param>
    /// <returns>The tileset.</returns>
    /// <exception cref="InvalidDataException">A line is malformed, a keyword is missing or repeated, or a tile lies outside the sheet.</exception>
    public static TilesetData Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);

        string? name = null, image = null;
        int tileWidth = 0, tileHeight = 0, columns = 0, tileCount = 0;
        HashSet<string> keywords = new(StringComparer.Ordinal);
        List<(int Line, int First, int Last, TileTraits Traits)> ranges = [];
        int line = 0;
        foreach (string raw in text.Split('\n'))
        {
            line++;
            string[] words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0].StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            string keyword = words[0];
            if (keyword == "tile")
            {
                ranges.Add(ParseTile(words, line, source));
                continue;
            }

            if (!keywords.Add(keyword))
            {
                throw SourceError.At(source, line, $"'{keyword}' is given twice");
            }

            switch (keyword)
            {
                case "name":
                    name = Single(words, line, source);
                    break;
                case "image":
                    image = Single(words, line, source);
                    break;
                case "tilewidth":
                    tileWidth = Positive(words, line, source);
                    break;
                case "tileheight":
                    tileHeight = Positive(words, line, source);
                    break;
                case "columns":
                    columns = Positive(words, line, source);
                    break;
                case "tilecount":
                    tileCount = Positive(words, line, source);
                    break;
                default:
                    throw SourceError.At(source, line, $"unknown keyword '{keyword}'");
            }
        }

        foreach (string required in new[] { "name", "image", "tilewidth", "tileheight", "columns", "tilecount" })
        {
            if (!keywords.Contains(required))
            {
                throw SourceError.At(source, 0, $"'{required}' is missing");
            }
        }

        if (!IsValidName(name!))
        {
            throw SourceError.At(source, 0, $"'{name}' is not a tileset name (letters, digits and underscores)");
        }

        Dictionary<int, TileTraits> traits = [];
        foreach ((int at, int first, int last, TileTraits flags) in ranges)
        {
            if (last >= tileCount)
            {
                throw SourceError.At(source, at, $"tile {last} is outside the sheet, which has {tileCount} tiles");
            }

            for (int i = first; i <= last; i++)
            {
                TileTraits merged = (traits.TryGetValue(i, out TileTraits had) ? had : TileTraits.None) | flags;
                if ((merged & TileTraits.Passable) != 0 && (merged & TileTraits.Impassable) != 0)
                {
                    throw SourceError.At(source, at, $"tile {i} is both Passable and Impassable");
                }

                traits[i] = merged;
            }
        }

        return new TilesetData(name!, image!, tileWidth, tileHeight, columns, tileCount, traits);
    }

    /// <summary>Reads the tileset data file of a name from a folder.</summary>
    /// <param name="folder">The folder of the tileset data files (<c>art/own/source/tilesets</c>).</param>
    /// <param name="name">The tileset name; the file is <c>&lt;name&gt;.tileset</c>.</param>
    /// <returns>The tileset.</returns>
    /// <exception cref="InvalidDataException">The name is not valid, the file is missing, or the name line does not match the file name.</exception>
    public static TilesetData Load(string folder, string name)
    {
        if (!IsValidName(name))
        {
            throw new InvalidDataException($"'{name}' is not a tileset name (letters, digits and underscores).");
        }

        string path = Path.Combine(folder, name + ".tileset");
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Tileset '{name}' has no data file {path}.");
        }

        TilesetData data = Parse(File.ReadAllText(path), path);
        if (!string.Equals(data.Name, name, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: the name line says '{data.Name}', but the file is named '{name}'.");
        }

        return data;
    }

    private static (int Line, int First, int Last, TileTraits Traits) ParseTile(string[] words, int line, string source)
    {
        if (words.Length < 3)
        {
            throw SourceError.At(source, line, "a tile line is 'tile <index or first-last> <trait>...'");
        }

        int dash = words[1].IndexOf('-', StringComparison.Ordinal);
        string firstText = dash < 0 ? words[1] : words[1][..dash];
        string lastText = dash < 0 ? words[1] : words[1][(dash + 1)..];
        if (!int.TryParse(firstText, NumberStyles.None, CultureInfo.InvariantCulture, out int first)
            || !int.TryParse(lastText, NumberStyles.None, CultureInfo.InvariantCulture, out int last)
            || first > last)
        {
            throw SourceError.At(source, line, $"'{words[1]}' is not a tile index or a first-last range");
        }

        TileTraits flags = TileTraits.None;
        foreach (string trait in words[2..])
        {
            flags |= trait switch
            {
                "Water" => TileTraits.Water,
                "Passable" => TileTraits.Passable,
                "Impassable" => TileTraits.Impassable,
                "Diggable" => TileTraits.Diggable,
                _ => throw SourceError.At(source, line, $"unknown trait '{trait}' (Water, Passable, Impassable, Diggable)"),
            };
        }

        return (line, first, last, flags);
    }

    private static string Single(string[] words, int line, string source)
        => words.Length == 2 ? words[1] : throw SourceError.At(source, line, $"'{words[0]}' takes exactly one value");

    private static int Positive(string[] words, int line, string source)
    {
        string value = Single(words, line, source);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0
            ? number
            : throw SourceError.At(source, line, $"'{words[0]}' must be a whole number above zero");
    }
}
