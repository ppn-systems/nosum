// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Maps;

/// <summary>What the server knows about one map: where players can walk, where they enter and where they can leave.</summary>
public sealed class MapDefinition
{
    /// <summary>Initializes a map definition.</summary>
    /// <param name="id">Stable map key, for example <c>farm</c>.</param>
    /// <param name="grid">Walkable tiles.</param>
    /// <param name="spawnX">Spawn feet x, world pixels.</param>
    /// <param name="spawnY">Spawn feet y, world pixels.</param>
    /// <param name="warps">The map's exits to other hosted maps; empty when none.</param>
    public MapDefinition(string id, WalkGrid grid, double spawnX, double spawnY, IReadOnlyList<MapWarp>? warps = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(grid);

        this.Id = id;
        this.Grid = grid;
        this.SpawnX = spawnX;
        this.SpawnY = spawnY;
        this.Warps = warps ?? [];
    }

    /// <summary>Gets the stable map key.</summary>
    public string Id { get; }

    /// <summary>Gets the walkable tiles.</summary>
    public WalkGrid Grid { get; }

    /// <summary>Gets the spawn feet x, world pixels.</summary>
    public double SpawnX { get; }

    /// <summary>Gets the spawn feet y, world pixels.</summary>
    public double SpawnY { get; }

    /// <summary>Gets the exits, with targets already resolved to hosted map ids.</summary>
    public IReadOnlyList<MapWarp> Warps { get; }
}
