// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Presence;
using Nosum.Domain.World;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.GroundItems;

/// <summary>An item lying on the ground, as the clients are told about it.</summary>
/// <param name="Id">Ground item id.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many.</param>
/// <param name="X">X on the map, pixels.</param>
/// <param name="Y">Y on the map, pixels.</param>
public readonly record struct GroundItemView(long Id, int ItemId, int Quantity, int X, int Y);

/// <summary>The ground items of one instance.</summary>
/// <param name="MapId">The map id; empty when the player is on no map.</param>
/// <param name="Items">The items.</param>
public sealed record GroundItemsView(string MapId, IReadOnlyList<GroundItemView> Items);

/// <summary>Where and what to drop (<see cref="IGroundItems"/>).</summary>
/// <param name="Instance">The place the item lands in.</param>
/// <param name="X">X on the map, pixels.</param>
/// <param name="Y">Y on the map, pixels.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many; split into stacks of at most the item's stack size.</param>
/// <param name="CausedBy">The account that caused the drop (it has priority for a few seconds on a shared map), or <see langword="null"/>.</param>
public readonly record struct GroundDrop(MapInstance Instance, int X, int Y, int ItemId, int Quantity, Guid? CausedBy);

/// <summary>
/// A drop whose rows were added to a caller's unit of work and wait for it to save (<see cref="IGroundItems.Stage"/>). Once the unit saved,
/// hand it to <see cref="IGroundItems.CompleteAsync"/>; if the unit failed, drop it.
/// </summary>
public sealed class PendingGroundDrop
{
    /// <summary>Initializes the pending drop.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="rows">The rows added to the unit of work.</param>
    public PendingGroundDrop(MapInstance instance, IReadOnlyList<GroundItem> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        this.Instance = instance;
        this.Rows = rows;
    }

    /// <summary>Gets the place.</summary>
    public MapInstance Instance { get; }

    /// <summary>Gets the rows added to the unit of work; their ids are known once it saved.</summary>
    public IReadOnlyList<GroundItem> Rows { get; }
}

/// <summary>
/// Items lying in the world (docs/adr/ADR-021 D5-D6): anything that "pops" an item (a harvest, a broken node) drops it here instead of
/// into an inventory, and a player walking within 1.5 tiles picks it up automatically. The server decides everything: the
/// distance is measured against the authoritative position and the move into the inventory is one atomic transfer
/// (<see cref="Economy.ITransferService"/>); a full inventory leaves the item where it is.
/// </summary>
public interface IGroundItems
{
    /// <summary>Drops an item on the ground in its own transaction and tells the instance.</summary>
    /// <param name="drop">What and where.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many ground items were created; zero for an unknown item, a quantity below one or a position off the map.</returns>
    Task<int> DropAsync(GroundDrop drop, CancellationToken cancellationToken = default);

    /// <summary>Adds the rows of a drop to a caller's unit of work, so the drop is saved with the caller's own change (a harvest resets the plot and drops the produce in one transaction).</summary>
    /// <param name="unit">The caller's unit of work; the caller saves it.</param>
    /// <param name="drop">What and where.</param>
    /// <returns>The pending drop, or <see langword="null"/> when nothing can be dropped.</returns>
    PendingGroundDrop? Stage(IUnitOfWork unit, GroundDrop drop);

    /// <summary>Makes a staged drop visible after its unit of work saved: files it in the instance and tells the players.</summary>
    /// <param name="pending">The result of <see cref="Stage"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the players were told.</returns>
    Task CompleteAsync(PendingGroundDrop pending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a player's authoritative position changed: picks up the items within reach, without blocking the caller. Cheap when nothing
    /// lies nearby (a lookup in the nine chunks around the player). Failures never reach the caller.
    /// </summary>
    /// <param name="player">The player's presence record.</param>
    /// <returns>A task that completes when the pickups finished; the movement path need not await it.</returns>
    Task OnPlayerMovedAsync(PlayerPresence player);

    /// <summary>Reads every ground item of the instance the player stands in, loading it first when needed.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The items; an empty map id when the player is on no map.</returns>
    Task<GroundItemsView> GetAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Removes the items that lay on shared maps for too long.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many vanished.</returns>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets an instance for good: drops it from memory and deletes its rows. Used for a mine floor when its player leaves it (docs/adr/ADR-026 D3),
    /// so nothing lies on a floor that no longer exists.
    /// </summary>
    /// <param name="instance">The place.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the rows are gone.</returns>
    Task ForgetAsync(MapInstance instance, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
