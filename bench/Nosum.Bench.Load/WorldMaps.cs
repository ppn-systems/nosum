// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Bench.Load;

/// <summary>One map as the bots need it: the same walk grid, exits and tillable tiles the server builds from the same .tmx file.</summary>
internal sealed record MapInfo(string Id, bool IsPrivate, WalkGrid Grid, IReadOnlyList<MapWarp> Warps, TillGrid? Tillable);

/// <summary>The world's maps, loaded the way the server loads them (<c>TmxMapCatalog</c>), and the routes between them.</summary>
internal sealed class WorldMaps
{
    private readonly Dictionary<string, MapInfo> _maps;

    private WorldMaps(Dictionary<string, MapInfo> maps) => _maps = maps;

    public IEnumerable<MapInfo> All => _maps.Values;

    /// <summary>Loads every map of <see cref="GameMaps"/> from a folder.</summary>
    /// <param name="mapsDirectory">Folder that holds the .tmx files.</param>
    /// <returns>The world.</returns>
    public static WorldMaps Load(string mapsDirectory)
    {
        Dictionary<string, MapInfo> maps = new(StringComparer.Ordinal);
        foreach (MapSpec spec in GameMaps.All)
        {
            TileMap tiles = TmxReader.Parse(File.ReadAllText(Path.Combine(mapsDirectory, spec.TmxFile)));
            WalkGrid grid = spec.BuildWalkGrid(tiles);
            maps[spec.Id] = new MapInfo(spec.Id, spec.IsPrivate, grid, spec.ResolveWarps(tiles), spec.Farmable ? TillGrid.FromMap(tiles, grid) : null);
        }

        return new WorldMaps(maps);
    }

    public MapInfo Get(string id) => _maps[id];

    public bool TryGet(string id, out MapInfo map)
    {
        bool found = _maps.TryGetValue(id, out MapInfo? value);
        map = value!;
        return found;
    }

    /// <summary>Finds the exits to take, in order, to get from one map to another (fewest maps; a breadth-first search over the exits).</summary>
    /// <param name="from">The map the player is on.</param>
    /// <param name="to">The map to reach.</param>
    /// <returns>The exits to walk onto, one per map left; empty when already there; null when there is no way.</returns>
    public List<MapWarp>? Route(string from, string to)
    {
        if (from == to)
        {
            return [];
        }

        Dictionary<string, (string Previous, MapWarp Via)> reached = new(StringComparer.Ordinal) { [from] = (string.Empty, default) };
        Queue<string> queue = new();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (!_maps.TryGetValue(current, out MapInfo? map))
            {
                continue;
            }

            foreach (MapWarp warp in map.Warps)
            {
                if (reached.ContainsKey(warp.TargetMap))
                {
                    continue;
                }

                reached[warp.TargetMap] = (current, warp);
                if (warp.TargetMap == to)
                {
                    List<MapWarp> route = [];
                    for (string at = to; at != from; at = reached[at].Previous)
                    {
                        route.Add(reached[at].Via);
                    }

                    route.Reverse();
                    return route;
                }

                queue.Enqueue(warp.TargetMap);
            }
        }

        return null;
    }

    /// <summary>The public, non-co-op maps a traveller may head for, Town twice as likely as the others (it is where the crowds go).</summary>
    /// <returns>The pool to draw from.</returns>
    public List<string> TravelTargets()
    {
        List<string> targets = [];
        foreach (MapSpec spec in GameMaps.All.Where(m => !m.IsPrivate && !m.IsCoop))
        {
            targets.Add(spec.Id);
            if (spec.Id == GameMaps.Town)
            {
                targets.Add(spec.Id);
            }
        }

        return targets;
    }
}
