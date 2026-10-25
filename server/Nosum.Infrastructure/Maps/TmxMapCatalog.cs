// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Nosum.Abstractions.Maps;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.Infrastructure.Maps;

/// <summary>
/// The maps of <see cref="GameMaps"/>, loaded once from their Tiled <c>.tmx</c> files. The ids are the
/// catalogue's fixed list and the files are names inside one folder, so a client-supplied id can never
/// be turned into a file path.
/// </summary>
public sealed class TmxMapCatalog : IMapCatalog
{
    private readonly Dictionary<string, MapDefinition> _maps = new(StringComparer.Ordinal);

    /// <summary>Loads the given maps.</summary>
    /// <param name="specs">The maps to host.</param>
    /// <param name="mapsDirectory">Folder that holds the <c>.tmx</c> files.</param>
    /// <exception cref="FileNotFoundException">A map file is missing.</exception>
    public TmxMapCatalog(IEnumerable<MapSpec> specs, string mapsDirectory)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentException.ThrowIfNullOrEmpty(mapsDirectory);

        // Two passes: a map's reserved tiles depend on where the other maps' exits arrive in it.
        List<(MapSpec Spec, TileMap Map, WalkGrid Grid, List<MapWarp> Warps)> loaded = [];
        foreach (MapSpec spec in specs)
        {
            TileMap map = TmxReader.Parse(File.ReadAllText(Path.Combine(mapsDirectory, spec.TmxFile)));
            loaded.Add((spec, map, spec.BuildWalkGrid(map), spec.ResolveWarps(map)));
        }

        foreach ((MapSpec spec, TileMap map, WalkGrid grid, List<MapWarp> warps) in loaded)
        {
            (double x, double y) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);
            TillGrid? tillable = spec.Farmable ? TillGrid.FromMap(map, grid) : null;
            HashSet<(int X, int Y)> reserved = ReservedTiles(spec, grid, warps, loaded);
            _maps.Add(spec.Id, new MapDefinition(spec.Id, grid, x, y, warps, tillable, spec.IsPrivate, spec.Placeable, reserved, spec.IsCoop, WaterGrid.FromMap(map), NodeSites.Read(map)));
        }
    }

    /// <inheritdoc/>
    public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
    {
        if (mapId is null)
        {
            map = null;
            return false;
        }

        return _maps.TryGetValue(mapId, out map);
    }

    // The door and the places visitors arrive, plus the tile north of each, stay clear so placed objects and resource nodes can never wall a player in
    // or block the way out.
    private static HashSet<(int X, int Y)> ReservedTiles(MapSpec spec, WalkGrid grid, List<MapWarp> warps, List<(MapSpec Spec, TileMap Map, WalkGrid Grid, List<MapWarp> Warps)> all)
    {
        HashSet<(int X, int Y)> reserved = [];
        (double spawnX, double spawnY) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);
        Reserve(reserved, WarpRule.TileX(spawnX, grid.TileSize), WarpRule.TileY(spawnY, grid.TileSize));

        foreach (MapWarp warp in warps)
        {
            if (WarpRule.TryEdge(warp, grid, out int edgeX, out int edgeY, out _))
            {
                Reserve(reserved, edgeX, edgeY);
            }
        }

        foreach ((MapSpec other, _, _, List<MapWarp> otherWarps) in all)
        {
            foreach (MapWarp warp in otherWarps)
            {
                if (string.Equals(warp.TargetMap, spec.Id, StringComparison.Ordinal))
                {
                    (double x, double y, _) = WarpRule.Arrival(grid, warp.TargetX, warp.TargetY);
                    Reserve(reserved, WarpRule.TileX(x, grid.TileSize), WarpRule.TileY(y, grid.TileSize));
                }
            }
        }

        return reserved;
    }

    private static void Reserve(HashSet<(int X, int Y)> reserved, int x, int y)
    {
        _ = reserved.Add((x, y));
        _ = reserved.Add((x, y - 1));
    }
}
