// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Inventory;
using Nosum.Contracts.Placeable;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// Chests (docs/adr/ADR-024 D4): 36-slot storage that is one more inventory owner. Only the owner of the place, or the members of the co-op on co-op land,
/// open one; a visitor cannot. Every move between the player and the chest is one atomic transfer, audited in the chest's log, and the chest's
/// contents are sent when it is opened (never with the placed objects) and then as pushes to its viewers.
/// </summary>
public interface IChestService
{
    /// <summary>Opens a chest and starts watching it.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Id of the placed chest.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the chest's slots.</returns>
    Task<ChestOpenOutcome> OpenAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default);

    /// <summary>Stops watching a chest; does nothing when the player was not.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Id of the placed chest.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the player stopped watching.</returns>
    ValueTask CloseAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default);

    /// <summary>Moves a stack, or part of it, between the player's inventory and an open chest, or inside the chest.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Id of the open chest.</param>
    /// <param name="fromSide">Where the stack is.</param>
    /// <param name="fromSlot">Its slot.</param>
    /// <param name="toSide">Where it goes.</param>
    /// <param name="toSlot">The slot it goes to, or a negative number for the first one that fits.</param>
    /// <param name="quantity">How many; zero or less means the whole stack.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or why nothing moved.</returns>
    Task<ChestStatus> MoveAsync(IConnection connection, int objectId, ChestSide fromSide, int fromSlot, ChestSide toSide, int toSlot, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Sorts an open chest: merges partial stacks and lays the stacks out by item kind and id.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="objectId">Id of the open chest.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or why the chest was not sorted.</returns>
    Task<ChestStatus> SortAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IChestService.OpenAsync"/>.</summary>
/// <param name="Status">Ok, or why the chest did not open.</param>
/// <param name="ObjectId">The chest's id.</param>
/// <param name="Capacity">Number of slots.</param>
/// <param name="Slots">The occupied slots.</param>
public sealed record ChestOpenOutcome(ChestStatus Status, int ObjectId, int Capacity, IReadOnlyList<InventorySlotView> Slots);
