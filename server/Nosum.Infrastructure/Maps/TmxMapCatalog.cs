// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Nosum.Abstractions.Maps;
using Nosum.Core;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Maps;

/// <summary>
/// Maps loaded once from Tiled <c>.tmx</c> files. Ids are a fixed list given by the composition
/// root, so a client-supplied id can never be turned into a file path.
/// </summary>
public sealed class TmxMapCatalog : IMapCatalog
{
    /// <summary>Name of the layer whose tiles block walking.</summary>
    public const string BlockingLayer = "Buildings";

    private readonly Dictionary<string, MapDefinition> _maps = new(StringComparer.Ordinal);

    /// <summary>Loads the given maps.</summary>
    /// <param name="sources">The maps to host.</param>
    /// <exception cref="FileNotFoundException">A map file is missing.</exception>
    public TmxMapCatalog(IEnumerable<TmxMapSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        foreach (TmxMapSource source in sources)
        {
            TileMap map = TmxReader.Parse(File.ReadAllText(source.Path));
            WalkGrid grid = WalkGrid.FromLayer(map, BlockingLayer);
            (double x, double y) = SpawnPoint.Find(grid, source.SpawnTileX, source.SpawnTileY);
            _maps.Add(source.Id, new MapDefinition(source.Id, grid, x, y));
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

/// <summary>A map file the server hosts.</summary>
/// <param name="Id">Stable map key.</param>
/// <param name="Path">Path of the <c>.tmx</c> file.</param>
/// <param name="SpawnTileX">Preferred spawn tile x.</param>
/// <param name="SpawnTileY">Preferred spawn tile y.</param>
public readonly record struct TmxMapSource(string Id, string Path, int SpawnTileX, int SpawnTileY);
