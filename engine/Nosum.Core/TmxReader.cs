// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Nosum.Core;

/// <summary>
/// Reads Tiled <c>.tmx</c> maps: orthogonal, embedded tilesets, CSV-encoded tile layers.
/// Tile flip bits are cleared. Read besides tiles: map and layer properties, and per-tileset tile
/// properties and animations. Object groups (including Tiled-style <c>TileData</c> objects) are not read.
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
        Dictionary<string, string> mapProps = [];
        Dictionary<string, string>? layerProps = null;
        int tileId = -1;
        List<int> frameTiles = [];
        List<int> frameDurations = [];

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (pendingSet is not null && reader.Depth == 1 && reader.Name == "tileset")
                {
                    tileSets.Add(pendingSet.Build());
                    pendingSet = null;
                }
                else if (pendingSet is not null && tileId >= 0 && reader.Depth == 2 && reader.Name == "tile")
                {
                    if (frameTiles.Count > 0)
                    {
                        pendingSet.Animations[tileId] = new TileAnimation([.. frameTiles], [.. frameDurations]);
                        frameTiles.Clear();
                        frameDurations.Clear();
                    }

                    tileId = -1;
                }

                continue;
            }

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
                    pendingSet.ImageSource = reader.GetAttribute("source") ?? throw new InvalidDataException($"Tileset '{pendingSet.Name}' has no image.");
                    break;

                case "tile" when pendingSet is not null && reader.Depth == 2:
                    tileId = Int(reader, "id");
                    break;

                case "frame" when pendingSet is not null && tileId >= 0 && reader.Depth == 4:
                    frameTiles.Add(Int(reader, "tileid"));
                    frameDurations.Add(Int(reader, "duration"));
                    break;

                case "property":
                    string? key = reader.GetAttribute("name");
                    string propValue = reader.GetAttribute("value") ?? string.Empty;
                    if (key is null)
                    {
                        break;
                    }

                    if (pendingSet is not null && tileId >= 0 && reader.Depth == 4)
                    {
                        pendingSet.AddProperty(tileId, key, propValue);
                    }
                    else if (layerProps is not null && reader.Depth == 3)
                    {
                        layerProps[key] = propValue;
                    }
                    else if (pendingSet is null && layerName is null && reader.Depth == 2)
                    {
                        mapProps[key] = propValue;
                    }

                    break;

                case "layer":
                    layerName = reader.GetAttribute("name") ?? string.Empty;
                    layerProps = [];
                    break;

                case "data" when layerName is not null:
                    if (!string.Equals(reader.GetAttribute("encoding"), "csv", StringComparison.Ordinal) || reader.GetAttribute("compression") is not null)
                    {
                        throw new InvalidDataException($"Layer '{layerName}': only uncompressed CSV data is supported.");
                    }

                    layers.Add(new TileLayer(layerName, ParseCsv(reader.ReadElementContentAsString(), width * height, layerName), layerProps is { Count: > 0 } ? layerProps : null));
                    layerName = null;
                    layerProps = null;
                    break;
            }
        }

        if (width <= 0 || height <= 0 || tileWidth <= 0 || tileHeight <= 0)
        {
            throw new InvalidDataException("Map size or tile size is missing.");
        }

        tileSets.Sort(static (a, b) => a.FirstGid.CompareTo(b.FirstGid));
        return new TileMap(width, height, tileWidth, tileHeight, tileSets, layers, mapProps.Count > 0 ? mapProps : null);
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

    private sealed class TileSetBuilder(int firstGid, string name, int columns, int tileCount)
    {
        private readonly Dictionary<int, List<KeyValuePair<string, string>>> _properties = [];

        public int FirstGid { get; } = firstGid;

        public string Name { get; } = name;

        public string? ImageSource { get; set; }

        public Dictionary<int, TileAnimation> Animations { get; } = [];

        public void AddProperty(int tile, string key, string value)
        {
            if (!_properties.TryGetValue(tile, out List<KeyValuePair<string, string>>? list))
            {
                _properties[tile] = list = [];
            }

            list.Add(new KeyValuePair<string, string>(key, value));
        }

        public TileSet Build()
        {
            Dictionary<int, KeyValuePair<string, string>[]> properties = [];
            foreach ((int id, List<KeyValuePair<string, string>> list) in _properties)
            {
                properties[id] = [.. list];
            }

            TileProperties? tiles = properties.Count > 0 || this.Animations.Count > 0 ? new TileProperties(tileCount, properties, this.Animations) : null;
            return new TileSet(this.FirstGid, this.Name, this.ImageSource ?? throw new InvalidDataException($"Tileset '{this.Name}' has no image."), columns, tileCount, tiles);
        }
    }
}
