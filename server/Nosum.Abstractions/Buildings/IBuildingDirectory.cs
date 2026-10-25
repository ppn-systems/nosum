// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Buildings;

/// <summary>One farm building as the world sees it: immutable once built (docs/adr/ADR-028).</summary>
/// <param name="Id">The building's id; the interior instance is keyed by it.</param>
/// <param name="ObjectId">The placed object standing on the land.</param>
/// <param name="Kind">The building kind.</param>
/// <param name="Land">The land it stands on: the player's farm or a co-op's land.</param>
/// <param name="TileX">Left tile of the footprint.</param>
/// <param name="TileY">Top tile of the footprint.</param>
/// <param name="CompletesAtMs">When it is finished, Unix milliseconds on the server clock.</param>
public sealed record BuildingInfo(Guid Id, int ObjectId, BuildingKind Kind, MapInstance Land, int TileX, int TileY, long CompletesAtMs)
{
    /// <summary>Gets the interior instance of this building.</summary>
    public MapInstance Interior => BuildingRules.InteriorOf(this.Kind, this.Id);

    /// <summary>Gets the door tile and the tile in front of it, in land tiles.</summary>
    public ((int X, int Y) Door, (int X, int Y) Front) Doors
        => BuildingCatalog.TryGet(this.Kind, out BuildingDefinition? definition) ? BuildingRules.DoorTiles(definition, this.TileX, this.TileY) : default;

    /// <summary>Returns the construction state at a time.</summary>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <returns>Finished or under construction.</returns>
    public BuildingState StateAt(long nowMs) => BuildingRules.StateOf(this.CompletesAtMs, nowMs);
}

/// <summary>
/// What the world knows about the buildings of every land in use: where each door is (so walking onto it can enter the interior), who owns an interior (so entry can be
/// decided) and which interiors belong to a player (so a revoked visitor can be sent out of them). Read without a lock on the movement path; filled when a land is
/// entered and replaced whenever its buildings change (docs/adr/ADR-028 D2).
/// </summary>
public interface IBuildingDirectory
{
    /// <summary>Loads the buildings of a land the first time it is entered.</summary>
    /// <param name="land">The land.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when they are known.</returns>
    ValueTask PrepareAsync(MapInstance land, CancellationToken cancellationToken = default);

    /// <summary>Returns the buildings of a land that is known; empty for any other.</summary>
    /// <param name="land">The land.</param>
    /// <returns>The buildings.</returns>
    IReadOnlyList<BuildingInfo> BuildingsOf(MapInstance land);

    /// <summary>Finds the building whose door is a tile of a land.</summary>
    /// <param name="land">The land.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <param name="building">The building when the tile is its door.</param>
    /// <returns><see langword="true"/> when the tile is a door.</returns>
    bool TryFindDoor(MapInstance land, int tileX, int tileY, [NotNullWhen(true)] out BuildingInfo? building);

    /// <summary>Finds a building by id, loading it when it is not known yet.</summary>
    /// <param name="buildingId">The id.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The building, or <see langword="null"/> when there is none.</returns>
    ValueTask<BuildingInfo?> FindAsync(Guid buildingId, CancellationToken cancellationToken = default);

    /// <summary>Returns the interior instances of the buildings standing on a player's farm.</summary>
    /// <param name="ownerId">The player's id.</param>
    /// <returns>The interiors of the buildings of the known farm; empty when the farm is not in use.</returns>
    IReadOnlyList<MapInstance> InteriorsOfPlayer(Guid ownerId);

    /// <summary>Forgets what is known about a land and loads it again, after a building was added.</summary>
    /// <param name="land">The land.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The buildings now.</returns>
    ValueTask<IReadOnlyList<BuildingInfo>> ReloadAsync(MapInstance land, CancellationToken cancellationToken = default);
}
