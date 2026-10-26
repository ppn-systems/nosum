// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Nosum.MapGen;

/// <summary>
/// Compiles a layout (docs/adr/ADR-042 D4) into a Tiled <c>.tmx</c>: a text grid with one character per tile, a legend that maps each character to a
/// tileset and a tile, layered sections and the map's properties (for example <c>Warp</c>). The format is documented in <c>tools/README.md</c>.
/// </summary>
internal static class LayoutCompiler
{
    /// <summary>The character of an empty cell.</summary>
    public const char Empty = '.';

    private const string LegendKeyword = "legend";
    private const string LayerKeyword = "layer";
    private const string PropertyKeyword = "property";

    /// <summary>Compiles a layout.</summary>
    /// <param name="text">The layout file content.</param>
    /// <param name="source">The file name, for error messages.</param>
    /// <param name="tilesetOf">Returns the data of a tileset a legend names.</param>
    /// <returns>The <c>.tmx</c> document.</returns>
    /// <exception cref="InvalidDataException">The layout is malformed; the message names the line.</exception>
    public static string Compile(string text, string source, Func<string, TilesetData> tilesetOf)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tilesetOf);

        List<LegendEntry> legend = [];
        Dictionary<char, LegendEntry> byGlyph = [];
        List<KeyValuePair<string, string>> properties = [];
        List<RawLayer> layers = [];
        bool inLegend = false;
        int line = 0;
        foreach (string raw in text.Split('\n'))
        {
            line++;
            string code = raw.Trim();
            if (code.Length == 0 || code.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            string[] words = code.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case LegendKeyword when words.Length == 1:
                    if (layers.Count > 0)
                    {
                        throw SourceError.At(source, line, "the legend must come before the first layer");
                    }

                    inLegend = true;
                    break;

                case PropertyKeyword:
                    if (words.Length < 3)
                    {
                        throw SourceError.At(source, line, "a property is 'property <Name> <value>'");
                    }

                    if (layers.Count > 0)
                    {
                        throw SourceError.At(source, line, "map properties must come before the first layer");
                    }

                    inLegend = false;
                    string afterName = code[words[0].Length..].TrimStart()[words[1].Length..].TrimStart();
                    properties.Add(new KeyValuePair<string, string>(words[1], afterName));
                    break;

                case LayerKeyword:
                    if (words.Length != 2)
                    {
                        throw SourceError.At(source, line, "a layer is 'layer <Name>'");
                    }

                    if (layers.Exists(l => string.Equals(l.Name, words[1], StringComparison.Ordinal)))
                    {
                        throw SourceError.At(source, line, $"layer '{words[1]}' is given twice");
                    }

                    inLegend = false;
                    layers.Add(new RawLayer(words[1], line));
                    break;

                default:
                    if (inLegend)
                    {
                        LegendEntry entry = ParseLegend(words, code, line, source, byGlyph);
                        legend.Add(entry);
                        byGlyph.Add(entry.Glyph, entry);
                    }
                    else if (layers.Count == 0)
                    {
                        throw SourceError.At(source, line, $"'{code}' comes before the first layer; a tile row follows a 'layer' line");
                    }
                    else if (words.Length != 1)
                    {
                        throw SourceError.At(source, line, "a tile row is one word of characters, with no spaces");
                    }
                    else
                    {
                        layers[^1].Rows.Add((line, words[0]));
                    }

                    break;
            }
        }

        if (layers.Count == 0)
        {
            throw SourceError.At(source, 0, "the layout has no layer");
        }

        if (legend.Count == 0)
        {
            throw SourceError.At(source, 0, "the layout has no legend");
        }

        // Tilesets get global ids in the order the legend first names them, so the same layout always gives the same ids.
        Dictionary<string, TilesetData> sets = [];
        Dictionary<string, int> firstGids = [];
        List<PlacedTileset> placed = [];
        Dictionary<char, int> gidOf = [];
        int nextGid = 1;
        foreach (LegendEntry entry in legend)
        {
            if (!sets.TryGetValue(entry.Tileset, out TilesetData? owner))
            {
                owner = Resolve(tilesetOf, entry, source);
                sets.Add(entry.Tileset, owner);
                firstGids.Add(entry.Tileset, nextGid);
                placed.Add(new PlacedTileset(owner, nextGid));
                nextGid += owner.TileCount;
            }

            if (entry.Index >= owner.TileCount)
            {
                throw SourceError.At(source, entry.Line, $"tile {entry.Index} is outside '{entry.Tileset}', which has {owner.TileCount} tiles");
            }

            gidOf.Add(entry.Glyph, firstGids[entry.Tileset] + entry.Index);
        }

        int height = layers[0].Rows.Count;
        int width = height == 0 ? 0 : layers[0].Rows[0].Row.Length;
        if (width == 0)
        {
            throw SourceError.At(source, layers[0].Line, $"layer '{layers[0].Name}' has no tile rows");
        }

        List<MapLayer> mapLayers = [];
        foreach (RawLayer layer in layers)
        {
            if (layer.Rows.Count != height)
            {
                throw SourceError.At(source, layer.Line, $"layer '{layer.Name}' has {layer.Rows.Count} rows, the first layer has {height}");
            }

            int[] gids = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                (int rowLine, string row) = layer.Rows[y];
                if (row.Length != width)
                {
                    throw SourceError.At(source, rowLine, $"the row has {row.Length} tiles, the first row has {width}");
                }

                for (int x = 0; x < width; x++)
                {
                    gids[(y * width) + x] = row[x] == Empty ? 0 : Gid(gidOf, row[x], rowLine, source);
                }
            }

            mapLayers.Add(new MapLayer(layer.Name, gids));
        }

        AutoTerrain.Apply(width, height, placed, mapLayers);
        return TmxWriter.Write(width, height, placed, mapLayers, properties);
    }

    private static LegendEntry ParseLegend(string[] words, string code, int line, string source, Dictionary<char, LegendEntry> byGlyph)
    {
        if (words.Length != 3 || words[0].Length != 1)
        {
            throw SourceError.At(source, line, $"a legend entry is '<character> <tileset> <tile index>', not '{code}'");
        }

        char glyph = words[0][0];
        if (glyph == Empty)
        {
            throw SourceError.At(source, line, $"'{Empty}' is the empty tile and cannot be in the legend");
        }

        if (byGlyph.ContainsKey(glyph))
        {
            throw SourceError.At(source, line, $"character '{glyph}' is already in the legend");
        }

        if (!TilesetData.IsValidName(words[1]))
        {
            throw SourceError.At(source, line, $"'{words[1]}' is not a tileset name (letters, digits and underscores)");
        }

        if (!int.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw SourceError.At(source, line, $"tile index '{words[2]}' is not a whole number");
        }

        return new LegendEntry(glyph, words[1], index, line);
    }

    private static TilesetData Resolve(Func<string, TilesetData> tilesetOf, LegendEntry entry, string source)
    {
        try
        {
            return tilesetOf(entry.Tileset);
        }
        catch (InvalidDataException ex)
        {
            throw SourceError.At(source, entry.Line, ex.Message);
        }
    }

    private static int Gid(Dictionary<char, int> gidOf, char glyph, int line, string source)
        => gidOf.TryGetValue(glyph, out int gid)
            ? gid
            : throw SourceError.At(source, line, $"character '{glyph}' has no legend entry");

    private sealed record LegendEntry(char Glyph, string Tileset, int Index, int Line);

    private sealed class RawLayer(string name, int line)
    {
        public string Name { get; } = name;

        public int Line { get; } = line;

        public List<(int Line, string Row)> Rows { get; } = [];
    }
}
