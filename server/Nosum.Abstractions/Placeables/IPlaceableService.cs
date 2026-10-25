// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// Objects players place on the ground of their own place (furniture in the farmhouse). The server decides everything: where the
/// player is, which item is in the slot, the reach and the free footprint never come from the client. A change is saved with the
/// inventory change in one transaction and pushed to everyone in the same instance (docs/adr/ADR-015).
/// </summary>
public interface IPlaceableService
{
    /// <summary>Reads the placed objects of the instance the connection's player is in.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The map id and the objects; an empty map id when the player is on no placeable map.</returns>
    Task<PlacedSnapshotView> GetAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Finds a placed object of an instance by id.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="objectId">Object id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The object; <see langword="null"/> when the instance has no such object or its map allows none.</returns>
    Task<PlacedItem?> FindAsync(MapInstance instance, int objectId, CancellationToken cancellationToken = default);

    /// <summary>Places the item in an inventory slot.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="tileX">Left tile of the footprint.</param>
    /// <param name="tileY">Top tile of the footprint.</param>
    /// <param name="rotation">Rotation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the new object's id.</returns>
    Task<PlaceOutcome> PlaceAsync(IConnection connection, int slot, int tileX, int tileY, int rotation, CancellationToken cancellationToken = default);

    /// <summary>Moves or rotates a placed object.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Object id.</param>
    /// <param name="tileX">New left tile.</param>
    /// <param name="tileY">New top tile.</param>
    /// <param name="rotation">New rotation.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the object's id.</returns>
    Task<PlaceOutcome> MoveAsync(IConnection connection, int objectId, int tileX, int tileY, int rotation, CancellationToken cancellationToken = default);

    /// <summary>Picks an object up into the inventory.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Object id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the object's id.</returns>
    Task<PlaceOutcome> PickUpAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a placeable request.</summary>
/// <param name="Status">Ok, or why nothing changed.</param>
/// <param name="ObjectId">The object placed, moved or picked up; zero on a refusal.</param>
public readonly record struct PlaceOutcome(PlaceStatus Status, int ObjectId);

/// <summary>The placed objects of one instance.</summary>
/// <param name="MapId">The map key; empty when the player is on no placeable map.</param>
/// <param name="Objects">The objects.</param>
public sealed record PlacedSnapshotView(string MapId, IReadOnlyList<PlacedItem> Objects);
