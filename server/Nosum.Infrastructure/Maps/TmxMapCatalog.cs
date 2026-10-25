// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Nosum.Abstractions.Maps;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

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

        foreach (MapSpec spec in specs)
        {
            TileMap map = TmxReader.Parse(File.ReadAllText(Path.Combine(mapsDirectory, spec.TmxFile)));
            WalkGrid grid = spec.BuildWalkGrid(map);
            (double x, double y) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);
            _maps.Add(spec.Id, new MapDefinition(spec.Id, grid, x, y, spec.ResolveWarps(map)));
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
}
