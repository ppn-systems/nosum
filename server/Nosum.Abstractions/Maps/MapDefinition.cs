// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Farm;
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
    /// <param name="tillable">Tiles a hoe works on; <see langword="null"/> for a map that cannot be farmed.</param>
    /// <param name="isPrivate">Whether every player has their own instance of the map.</param>
    /// <param name="placeable">Whether players may place furniture on it.</param>
    /// <param name="reservedTiles">Tiles that must stay clear of placed objects (exits and arrival points).</param>
    /// <param name="isCoop">Whether each co-op has its own instance of the map (docs/adr/ADR-020).</param>
    public MapDefinition(string id, WalkGrid grid, double spawnX, double spawnY, IReadOnlyList<MapWarp>? warps = null, TillGrid? tillable = null, bool isPrivate = false, bool placeable = false, IReadOnlySet<(int X, int Y)>? reservedTiles = null, bool isCoop = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(grid);

        this.Id = id;
        this.Grid = grid;
        this.SpawnX = spawnX;
        this.SpawnY = spawnY;
        this.Warps = warps ?? [];
        this.Tillable = tillable;
        this.IsPrivate = isPrivate;
        this.Placeable = placeable;
        this.ReservedTiles = reservedTiles ?? new HashSet<(int X, int Y)>();
        this.IsCoop = isCoop;
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

    /// <summary>Gets the tiles a hoe works on; <see langword="null"/> when the map cannot be farmed.</summary>
    public TillGrid? Tillable { get; }

    /// <summary>Gets whether every player has their own instance of this map (docs/adr/ADR-015).</summary>
    public bool IsPrivate { get; }

    /// <summary>Gets whether every co-op has its own instance of this map, owned by the co-op and open to its members only (docs/adr/ADR-020).</summary>
    public bool IsCoop { get; }

    /// <summary>Gets whether players may place objects on this map.</summary>
    public bool Placeable { get; }

    /// <summary>Gets the tiles placed objects must leave clear, so the way in and out is never blocked.</summary>
    public IReadOnlySet<(int X, int Y)> ReservedTiles { get; }
}
