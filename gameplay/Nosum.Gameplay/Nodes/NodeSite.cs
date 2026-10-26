// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;

namespace Nosum.Gameplay.Nodes;

/// <summary>A place on a map where the map file says something grows or lies: its tile and what the <c>Paths</c> layer marks there.</summary>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="Kind">What the marker stands for.</param>
/// <param name="Variant">The tree species, for a tree marker.</param>
public readonly record struct NodeSite(int X, int Y, NodeKind Kind, byte Variant);

/// <summary>
/// Reads node sites from the <c>Paths</c> layer of a Tiled map (docs/adr/ADR-021 D4a).
/// <para>
/// The layer uses the tile sheet named <c>Paths</c> (<c>assets/Maps/markers.png</c>, drawn by Nosum.MapGen; the layer is never drawn). The
/// meaning of each tile index was read off that sheet and checked against the maps (<c>Farm</c>, <c>Forest</c>, <c>Town</c>, <c>Backwoods</c>,
/// <c>BusStop</c>), where trees sit in the woods and stones, twigs and weeds in the clearings:
/// </para>
/// <list type="table">
/// <listheader><term>Index</term><description>Marker, and the kind Nosum makes of it</description></listheader>
/// <item><term>9, 10, 11</term><description>tree types 1, 2, 3 (the sheet draws "1", "2", a pine "3") → grown oak, maple, pine</description></item>
/// <item><term>13, 14, 15</term><description>three weed sprites → <see cref="NodeKind.Weed"/></description></item>
/// <item><term>16, 17</term><description>small and large stones → <see cref="NodeKind.Stone"/></description></item>
/// <item><term>18</term><description>a branch → <see cref="NodeKind.Twig"/></description></item>
/// <item><term>22</term><description>grass strokes → <see cref="NodeKind.Grass"/></description></item>
/// </list>
/// <para>
/// Every other index is ignored by <see cref="Read"/> on purpose: 0-8, 20, 21, 23 and 36 (spawn areas, a meteor, a stump or log clump and animal markers that need
/// resource clumps Nosum does not have), 12, 31, 32 and 34 (palm and other tree species), 27 and 35 (fish spots and a random marker), 19 (a large log) and 28-30, 33 (monster, fence and other markers).
/// Indices 24, 25 and 26 mark where something is foraged; <see cref="ReadForage"/> reads them for the daily forage spawn (docs/adr/ADR-025 D4).
/// </para>
/// </summary>
public static class NodeSites
{
    /// <summary>Name of the tile layer that holds the markers.</summary>
    public const string LayerName = "Paths";

    /// <summary>Name of the tile set the markers come from.</summary>
    public const string TileSetName = "Paths";

    /// <summary>Tells whether a <c>Paths</c> tile index marks a forage spot (the three flower and berry glyphs of the sheet's sixth row).</summary>
    /// <param name="index">Tile index within the <c>Paths</c> sheet.</param>
    /// <returns><see langword="true"/> for indices 24, 25 and 26.</returns>
    public static bool IsForageIndex(int index) => index is 24 or 25 or 26;

    /// <summary>Maps a <c>Paths</c> tile index to what it stands for.</summary>
    /// <param name="index">Tile index within the <c>Paths</c> sheet (gid minus the set's first gid).</param>
    /// <param name="kind">The node kind when the index is understood.</param>
    /// <param name="variant">The tree species for a tree, otherwise zero.</param>
    /// <returns><see langword="true"/> when the index is one Nosum places a node for.</returns>
    public static bool TryMap(int index, out NodeKind kind, out byte variant)
    {
        variant = 0;
        switch (index)
        {
            case 9:
                kind = NodeKind.Tree;
                variant = TreeVariant.Oak;
                return true;
            case 10:
                kind = NodeKind.Tree;
                variant = TreeVariant.Maple;
                return true;
            case 11:
                kind = NodeKind.Tree;
                variant = TreeVariant.Pine;
                return true;
            case 13 or 14 or 15:
                kind = NodeKind.Weed;
                return true;
            case 16 or 17:
                kind = NodeKind.Stone;
                return true;
            case 18:
                kind = NodeKind.Twig;
                return true;
            case 22:
                kind = NodeKind.Grass;
                return true;
            default:
                kind = NodeKind.None;
                return false;
        }
    }

    /// <summary>Reads every marker of a map, row by row.</summary>
    /// <param name="map">The parsed map.</param>
    /// <returns>The sites; empty when the map has no <c>Paths</c> layer or tile set.</returns>
    public static List<NodeSite> Read(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        List<NodeSite> sites = [];
        if (map.FindLayer(LayerName) is not TileLayer layer)
        {
            return sites;
        }

        int firstGid = -1;
        int tileCount = 0;
        foreach (TileSet set in map.TileSets)
        {
            if (string.Equals(set.Name, TileSetName, StringComparison.Ordinal))
            {
                firstGid = set.FirstGid;
                tileCount = set.TileCount;
                break;
            }
        }

        if (firstGid < 0)
        {
            return sites;
        }

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int index = layer.Gids[(y * map.Width) + x] - firstGid;
                if (index >= 0 && index < tileCount && TryMap(index, out NodeKind kind, out byte variant))
                {
                    sites.Add(new NodeSite(x, y, kind, variant));
                }
            }
        }

        return sites;
    }

    /// <summary>Reads the forage spots of a map, row by row (docs/adr/ADR-025 D4).</summary>
    /// <param name="map">The parsed map.</param>
    /// <returns>The spots as <see cref="NodeKind.Forage"/> sites with variant zero; empty when the map has no <c>Paths</c> layer or tile set.</returns>
    public static List<NodeSite> ReadForage(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        List<NodeSite> sites = [];
        if (map.FindLayer(LayerName) is not TileLayer layer)
        {
            return sites;
        }

        TileSet? paths = null;
        foreach (TileSet set in map.TileSets)
        {
            if (string.Equals(set.Name, TileSetName, StringComparison.Ordinal))
            {
                paths = set;
                break;
            }
        }

        if (paths is null)
        {
            return sites;
        }

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int index = layer.Gids[(y * map.Width) + x] - paths.FirstGid;
                if (index >= 0 && index < paths.TileCount && IsForageIndex(index))
                {
                    sites.Add(new NodeSite(x, y, NodeKind.Forage, 0));
                }
            }
        }

        return sites;
    }
}
