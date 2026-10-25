// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Nosum.Core;

/// <summary>
/// Reads Tiled <c>.tmx</c> maps: orthogonal, embedded tilesets, CSV-encoded tile layers.
/// Tile flip bits are cleared; object groups and properties are not read.
/// </summary>
public static class TmxReader
{
    private const uint FlipMask = 0x1FFFFFFF;

    /// <summary>
    /// Parses a map.
    /// </summary>
    /// <param name="xml">The <c>.tmx</c> file content.</param>
    /// <returns>The map.</returns>
    /// <exception cref="InvalidDataException">The map uses a feature this reader does not support.</exception>
    public static TileMap Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true });

        int width = 0, height = 0, tileWidth = 0, tileHeight = 0;
        List<TileSet> tileSets = [];
        List<TileLayer> layers = [];
        TileSetBuilder? pendingSet = null;
        string? layerName = null;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.Name)
            {
                case "map":
                    if (!string.Equals(reader.GetAttribute("orientation"), "orthogonal", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Only orthogonal maps are supported.");
                    }

                    width = Int(reader, "width");
                    height = Int(reader, "height");
                    tileWidth = Int(reader, "tilewidth");
                    tileHeight = Int(reader, "tileheight");
                    break;

                case "tileset" when reader.Depth == 1:
                    if (reader.GetAttribute("source") is not null)
                    {
                        throw new InvalidDataException("External tilesets (.tsx) are not supported.");
                    }

                    pendingSet = new TileSetBuilder(
                        Int(reader, "firstgid"),
                        reader.GetAttribute("name") ?? string.Empty,
                        Int(reader, "columns"),
                        Int(reader, "tilecount"));
                    break;

                case "image" when pendingSet is not null && reader.Depth == 2:
                    tileSets.Add(new TileSet(
                        pendingSet.FirstGid,
                        pendingSet.Name,
                        reader.GetAttribute("source") ?? throw new InvalidDataException($"Tileset '{pendingSet.Name}' has no image."),
                        pendingSet.Columns,
                        pendingSet.TileCount));
                    pendingSet = null;
                    break;

                case "layer":
                    layerName = reader.GetAttribute("name") ?? string.Empty;
                    break;

                case "data" when layerName is not null:
                    if (!string.Equals(reader.GetAttribute("encoding"), "csv", StringComparison.Ordinal) || reader.GetAttribute("compression") is not null)
                    {
                        throw new InvalidDataException($"Layer '{layerName}': only uncompressed CSV data is supported.");
                    }

                    layers.Add(new TileLayer(layerName, ParseCsv(reader.ReadElementContentAsString(), width * height, layerName)));
                    layerName = null;
                    break;
            }
        }

        if (width <= 0 || height <= 0 || tileWidth <= 0 || tileHeight <= 0)
        {
            throw new InvalidDataException("Map size or tile size is missing.");
        }

        tileSets.Sort(static (a, b) => a.FirstGid.CompareTo(b.FirstGid));
        return new TileMap(width, height, tileWidth, tileHeight, tileSets, layers);
    }

    private static int[] ParseCsv(string csv, int expected, string layer)
    {
        int[] gids = new int[expected];
        int count = 0;
        ReadOnlySpan<char> rest = csv.AsSpan();
        while (!rest.IsEmpty)
        {
            int comma = rest.IndexOf(',');
            ReadOnlySpan<char> item = (comma < 0 ? rest : rest[..comma]).Trim();
            rest = comma < 0 ? [] : rest[(comma + 1)..];
            if (item.IsEmpty)
            {
                continue;
            }

            if (count == expected)
            {
                throw new InvalidDataException($"Layer '{layer}' has more than {expected} tiles.");
            }

            gids[count++] = (int)(uint.Parse(item, NumberStyles.None, CultureInfo.InvariantCulture) & FlipMask);
        }

        if (count != expected)
        {
            throw new InvalidDataException($"Layer '{layer}' has {count} tiles, expected {expected}.");
        }

        return gids;
    }

    private static int Int(XmlReader reader, string name)
        => int.Parse(
            reader.GetAttribute(name) ?? throw new InvalidDataException($"<{reader.Name}> lacks '{name}'."),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);

    private sealed record TileSetBuilder(int FirstGid, string Name, int Columns, int TileCount);
}
