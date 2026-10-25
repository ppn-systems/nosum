// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Maps;

/// <summary>What the server knows about one map: where players can walk and where they enter.</summary>
public sealed class MapDefinition
{
    /// <summary>Initializes a map definition.</summary>
    /// <param name="id">Stable map key, for example <c>farm</c>.</param>
    /// <param name="grid">Walkable tiles.</param>
    /// <param name="spawnX">Spawn feet x, world pixels.</param>
    /// <param name="spawnY">Spawn feet y, world pixels.</param>
    public MapDefinition(string id, WalkGrid grid, double spawnX, double spawnY)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(grid);

        this.Id = id;
        this.Grid = grid;
        this.SpawnX = spawnX;
        this.SpawnY = spawnY;
    }

    /// <summary>Gets the stable map key.</summary>
    public string Id { get; }

    /// <summary>Gets the walkable tiles.</summary>
    public WalkGrid Grid { get; }

    /// <summary>Gets the spawn feet x, world pixels.</summary>
    public double SpawnX { get; }

    /// <summary>Gets the spawn feet y, world pixels.</summary>
    public double SpawnY { get; }
}
