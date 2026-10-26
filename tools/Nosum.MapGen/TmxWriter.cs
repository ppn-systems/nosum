// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using Nosum.Core;

namespace Nosum.MapGen;

/// <summary>A tileset placed in a map, with the global id its first tile has there.</summary>
/// <param name="Set">The tileset.</param>
/// <param name="FirstGid">The global tile id of the tileset's tile zero.</param>
internal sealed record PlacedTileset(TilesetData Set, int FirstGid);

/// <summary>A tile layer: its name and one global tile id per cell, row by row (zero is an empty cell).</summary>
/// <param name="Name">The layer name, for example <c>Back</c>.</param>
/// <param name="Gids">The ids, <c>width × height</c> of them.</param>
internal sealed record MapLayer(string Name, int[] Gids);

/// <summary>
/// Writes a Tiled <c>.tmx</c> document in the form the game's loader reads: orthogonal, embedded tilesets, CSV tile data, the traits of each
/// tileset as tile properties and the map's own properties. The output depends only on its arguments, so the same map always gives the same bytes.
/// </summary>
internal static class TmxWriter
{
    /// <summary>Writes a map.</summary>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="tilesets">The tilesets the layers use; all share one tile size.</param>
    /// <param name="layers">The tile layers, bottom first.</param>
    /// <param name="properties">Map properties, in file order.</param>
    /// <returns>The document, with LF line ends.</returns>
    /// <exception cref="InvalidDataException">No tileset is given, tile sizes differ, or a layer does not cover the map.</exception>
    public static string Write(int width, int height, IReadOnlyList<PlacedTileset> tilesets, IReadOnlyList<MapLayer> layers, IReadOnlyList<KeyValuePair<string, string>> properties)
    {
        ArgumentNullException.ThrowIfNull(tilesets);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(properties);
        if (tilesets.Count == 0)
        {
            throw new InvalidDataException("A map needs at least one tileset.");
        }

        TilesetData first = tilesets[0].Set;
        foreach (PlacedTileset placed in tilesets)
        {
            if (placed.Set.TileWidth != first.TileWidth || placed.Set.TileHeight != first.TileHeight)
            {
                throw new InvalidDataException($"Tileset '{placed.Set.Name}' has a different tile size from '{first.Name}'.");
            }
        }

        foreach (MapLayer layer in layers)
        {
            if (layer.Gids.Length != width * height)
            {
                throw new InvalidDataException($"Layer '{layer.Name}' has {layer.Gids.Length} cells, the map has {width * height}.");
            }
        }

        StringBuilder text = new();
        text.Append("<?xml version=\"1.0\"?>\n");
        text.Append(FormattableString.Invariant($"<map version=\"1.4\" tiledversion=\"1.4.2\" orientation=\"orthogonal\" renderorder=\"right-down\" compressionlevel=\"0\" width=\"{width}\" height=\"{height}\" tilewidth=\"{first.TileWidth}\" tileheight=\"{first.TileHeight}\" infinite=\"0\" nextlayerid=\"{layers.Count + 1}\" nextobjectid=\"1\">\n"));
        AppendProperties(text, "  ", properties);
        foreach (PlacedTileset placed in tilesets)
        {
            AppendTileset(text, placed);
        }

        for (int i = 0; i < layers.Count; i++)
        {
            AppendLayer(text, i + 1, layers[i], width, height);
        }

        text.Append("</map>\n");
        return text.ToString();
    }

    /// <summary>Escapes a value for an XML attribute.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static void AppendProperties(StringBuilder text, string indent, IReadOnlyList<KeyValuePair<string, string>> properties)
    {
        if (properties.Count == 0)
        {
            text.Append(indent).Append("<properties />\n");
            return;
        }

        text.Append(indent).Append("<properties>\n");
        foreach (KeyValuePair<string, string> property in properties)
        {
            text.Append(indent).Append("  ").Append(FormattableString.Invariant($"<property name=\"{Escape(property.Key)}\" type=\"string\" value=\"{Escape(property.Value)}\" />\n"));
        }

        text.Append(indent).Append("</properties>\n");
    }

    private static void AppendTileset(StringBuilder text, PlacedTileset placed)
    {
        TilesetData set = placed.Set;
        text.Append(FormattableString.Invariant($"  <tileset firstgid=\"{placed.FirstGid}\" name=\"{Escape(set.Name)}\" tilewidth=\"{set.TileWidth}\" tileheight=\"{set.TileHeight}\" tilecount=\"{set.TileCount}\" columns=\"{set.Columns}\">\n"));
        text.Append(FormattableString.Invariant($"    <image source=\"{Escape(set.Image)}\" width=\"{set.Columns * set.TileWidth}\" height=\"{set.Rows * set.TileHeight}\" />\n"));
        foreach (int id in set.Traits.Keys.Order())
        {
            text.Append(FormattableString.Invariant($"    <tile id=\"{id}\">\n      <properties>\n"));
            AppendTrait(text, set.Traits[id]);
            text.Append("      </properties>\n    </tile>\n");
        }

        text.Append("  </tileset>\n");
    }

    // The properties the rules read (TileProperties.ComputeFlags): Water true, Passable true or false, Diggable true.
    private static void AppendTrait(StringBuilder text, TileTraits traits)
    {
        if ((traits & TileTraits.Water) != 0)
        {
            text.Append("        <property name=\"Water\" type=\"string\" value=\"T\" />\n");
        }

        if ((traits & TileTraits.Passable) != 0)
        {
            text.Append("        <property name=\"Passable\" type=\"string\" value=\"T\" />\n");
        }
        else if ((traits & TileTraits.Impassable) != 0)
        {
            text.Append("        <property name=\"Passable\" type=\"string\" value=\"F\" />\n");
        }

        if ((traits & TileTraits.Diggable) != 0)
        {
            text.Append("        <property name=\"Diggable\" type=\"string\" value=\"T\" />\n");
        }
    }

    private static void AppendLayer(StringBuilder text, int id, MapLayer layer, int width, int height)
    {
        text.Append(FormattableString.Invariant($"  <layer id=\"{id}\" name=\"{Escape(layer.Name)}\" width=\"{width}\" height=\"{height}\" opacity=\"1\" offsetx=\"0\" offsety=\"0\">\n"));
        text.Append("   <data encoding=\"csv\">\n");
        for (int y = 0; y < height; y++)
        {
            text.Append(string.Join(',', layer.Gids.Skip(y * width).Take(width).Select(g => g.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            text.Append(y < height - 1 ? ",\n" : "\n");
        }

        text.Append("   </data>\n  </layer>\n");
    }
}
