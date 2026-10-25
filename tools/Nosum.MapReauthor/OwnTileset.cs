// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Nosum.Core;

namespace Nosum.MapReauthor;

/// <summary>
/// The layout of <c>own_terrain.png</c>, the tileset the re-authored maps draw terrain from: first the fill tiles (four texture variants of
/// each kind and walking trait), then the 16 corner tiles of every <see cref="WangSet"/>. A fill carries the traits (<c>Water</c>,
/// <c>Diggable</c>, <c>Passable</c>) of the placeholder tile it replaced, so collision, tilling and water refills are unchanged. The corner
/// tiles carry none: they are drawn on their own layer above the fills, only where terrains meet.
/// </summary>
internal static class OwnTileset
{
    /// <summary>The tileset image name the map names, without extension.</summary>
    public const string Name = "own_terrain";

    /// <summary>Tiles per row of the image.</summary>
    public const int Columns = 16;

    /// <summary>Side of a tile in pixels.</summary>
    public const int TileSize = 16;

    /// <summary>Texture variants of every fill.</summary>
    public const int Variants = 4;

    private static readonly TileTraits[] s_landTraits = [TileTraits.None, TileTraits.Diggable, TileTraits.Impassable, TileTraits.Passable];
    private static readonly TileTraits[] s_waterTraits = [.. s_landTraits, TileTraits.Water, TileTraits.Water | TileTraits.Impassable];

    /// <summary>Gets the number of fill tiles.</summary>
    public static int FillCount { get; } = (((TerrainKinds.Fills.Length - 1) * s_landTraits.Length) + s_waterTraits.Length) * Variants;

    /// <summary>Gets the number of tiles in the tileset.</summary>
    public static int TileCount => FillCount + (WangSet.All.Length * WangSet.Tiles);

    /// <summary>Gets the rows of the image.</summary>
    public static int Rows => (TileCount + Columns - 1) / Columns;

    /// <summary>Returns the traits a fill of a kind is drawn with.</summary>
    /// <param name="kind">The fill kind.</param>
    /// <returns>The supported trait sets.</returns>
    public static TileTraits[] TraitsOf(TerrainKind kind) => kind == TerrainKind.Water ? s_waterTraits : s_landTraits;

    /// <summary>Returns the local tile index of a fill.</summary>
    /// <param name="kind">The fill kind.</param>
    /// <param name="traits">The traits the cell must keep.</param>
    /// <param name="variant">The texture variant, 0 to <see cref="Variants"/> - 1.</param>
    /// <returns>The index, or -1 when the kind has no fill with these traits (the cell then stays on its placeholder).</returns>
    public static int Fill(TerrainKind kind, TileTraits traits, int variant)
    {
        int index = 0;
        foreach (TerrainKind fill in TerrainKinds.Fills)
        {
            TileTraits[] list = TraitsOf(fill);
            if (fill == kind)
            {
                int slot = Array.IndexOf(list, traits);
                return slot < 0 ? -1 : ((index + slot) * Variants) + (variant & (Variants - 1));
            }

            index += list.Length;
        }

        return -1;
    }

    /// <summary>Returns the local tile index of a corner tile.</summary>
    /// <param name="set">The index in <see cref="WangSet.All"/>.</param>
    /// <param name="code">The corner code (<see cref="WangSet.Code"/>).</param>
    /// <returns>The index.</returns>
    public static int Corner(int set, int code) => FillCount + (set * WangSet.Tiles) + code;

    /// <summary>Returns the <c>&lt;tileset&gt;</c> element: the tileset's size and the traits of every fill, as the placeholder maps write them.</summary>
    /// <param name="firstGid">The first global tile id the map gives the tileset.</param>
    /// <returns>The element, LF line endings, indented as the placeholder maps are.</returns>
    public static string TilesetElement(int firstGid)
    {
        StringBuilder xml = new();
        _ = xml.Append(CultureInfo.InvariantCulture, $"  <tileset firstgid=\"{firstGid}\" name=\"{Name}\" tilewidth=\"{TileSize}\" tileheight=\"{TileSize}\" tilecount=\"{TileCount}\" columns=\"{Columns}\">\n");
        _ = xml.Append(CultureInfo.InvariantCulture, $"    <image source=\"{Name}\" width=\"{Columns * TileSize}\" height=\"{Rows * TileSize}\" />\n");
        int index = 0;
        foreach (TerrainKind kind in TerrainKinds.Fills)
        {
            foreach (TileTraits traits in TraitsOf(kind))
            {
                List<string> properties = [];
                if ((traits & TileTraits.Water) != 0)
                {
                    properties.Add("Water\" type=\"string\" value=\"T");
                }

                if ((traits & TileTraits.Diggable) != 0)
                {
                    properties.Add("Diggable\" type=\"string\" value=\"T");
                }

                if ((traits & TileTraits.Passable) != 0)
                {
                    properties.Add("Passable\" type=\"string\" value=\"T");
                }

                if ((traits & TileTraits.Impassable) != 0)
                {
                    properties.Add("Passable\" type=\"string\" value=\"F");
                }

                for (int v = 0; v < Variants; v++, index++)
                {
                    if (properties.Count == 0)
                    {
                        continue;
                    }

                    _ = xml.Append(CultureInfo.InvariantCulture, $"    <tile id=\"{index}\">\n      <properties>\n");
                    foreach (string property in properties)
                    {
                        _ = xml.Append(CultureInfo.InvariantCulture, $"        <property name=\"{property}\" />\n");
                    }

                    _ = xml.Append("      </properties>\n    </tile>\n");
                }
            }
        }

        _ = xml.Append("  </tileset>\n");
        return xml.ToString();
    }
}
