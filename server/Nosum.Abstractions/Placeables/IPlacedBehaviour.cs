// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// What a kind of placed object does beyond standing there (a chest holds items, a furnace holds a job). A behaviour joins the pick-up of its objects, so
/// a chest with items or a furnace with a job is never destroyed with its contents inside (docs/adr/ADR-024 D4).
/// </summary>
public interface IPlacedBehaviour
{
    /// <summary>Gets the kind of object the behaviour belongs to.</summary>
    PlaceableKind Kind { get; }

    /// <summary>Returns the inventory owners the pick-up must lock together with the player (a chest's own inventory).</summary>
    /// <param name="item">The piece being picked up.</param>
    /// <returns>The owner ids; empty when the object keeps no inventory.</returns>
    IReadOnlyList<Guid> OwnersOf(PlacedItem item);

    /// <summary>Joins the pick-up transaction: refuse when the object is in use, or remove the object's own rows.</summary>
    /// <param name="item">The piece being picked up.</param>
    /// <param name="unit">The unit of work the pick-up is saved with.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see cref="PlaceStatus.Ok"/> to go ahead, or the reason to keep the object.</returns>
    Task<PlaceStatus> JoinPickUpAsync(PlacedItem item, IUnitOfWork unit, CancellationToken cancellationToken);

    /// <summary>Runs after a pick-up was committed.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="item">The piece that was picked up.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the behaviour has cleaned up (closed the chest's viewers).</returns>
    ValueTask PickedUpAsync(MapInstance instance, PlacedItem item, CancellationToken cancellationToken);
}
