// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Inventory;

namespace Nosum.Abstractions.Inventory;

/// <summary>Joins another change to the transaction of an inventory change; return <see langword="false"/> to refuse and roll everything back.</summary>
/// <param name="unit">The unit of work the inventory change is saved with.</param>
/// <param name="cancellationToken">Cancels the work.</param>
/// <returns>A task whose result says whether the combined change may be saved.</returns>
public delegate Task<bool> InventoryEnlistment(IUnitOfWork unit, CancellationToken cancellationToken);

/// <summary>
/// The one way to change a player's items. Every feature that gives or takes items (farming,
/// market, co-op warehouse, quests) calls it instead of touching inventory rows: changes are atomic
/// (all operations apply or none), serialized per player, logged for audit, and pushed to the player.
/// </summary>
public interface IInventoryService
{
    /// <summary>Loads the inventory; a player who has none yet gets one with the starter kit.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The capacity, the held hotbar slot and the occupied slots.</returns>
    Task<InventoryView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies operations all-or-nothing, writes one audit entry per item changed and pushes the changed
    /// slots to the player. When <paramref name="enlist"/> is given it runs inside the same transaction
    /// after the operations succeeded, so a feature can change its own data (a farm tile, a market
    /// listing) and the items together.
    /// </summary>
    /// <param name="playerId">The player whose inventory changes.</param>
    /// <param name="operations">What to add and remove, applied in order.</param>
    /// <param name="reason">Stable code for the audit trail, for example <c>farm.harvest</c>; never display text.</param>
    /// <param name="enlist">Extra work for the same transaction, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the slots that changed; a refusal changes nothing.</returns>
    Task<InventoryChange> ApplyAsync(
        Guid playerId,
        IReadOnlyList<InventoryOperation> operations,
        string reason,
        InventoryEnlistment? enlist = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the inventory changes of several players in one transaction (all players change or none), serialized against every
    /// other change of any of them. This is the base of trades, market purchases and the warehouse: items move between players
    /// together with whatever the <paramref name="enlist"/> callback changes (coins, a listing) or nothing does.
    /// </summary>
    /// <param name="changes">One entry per player, players distinct; operations are applied in order per player.</param>
    /// <param name="reason">Stable code for the audit trail; never display text.</param>
    /// <param name="enlist">Extra work for the same transaction, or <see langword="null"/>; return <see langword="false"/> to refuse.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or why nothing changed (an enlistment that refused is <see cref="InventoryStatus.Conflict"/>; the caller knows why).</returns>
    Task<InventoryStatus> ApplyManyAsync(
        IReadOnlyList<PlayerInventoryChange> changes,
        string reason,
        InventoryEnlistment? enlist = null,
        CancellationToken cancellationToken = default);

    /// <summary>Adds items (all of them or none).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="itemId">Item id from the catalogue.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="reason">Stable audit code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the changed slots.</returns>
    Task<InventoryChange> AddAsync(Guid playerId, int itemId, int quantity, string reason, CancellationToken cancellationToken = default);

    /// <summary>Removes items (all of them or none).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="itemId">Item id from the catalogue.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="reason">Stable audit code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the changed slots.</returns>
    Task<InventoryChange> RemoveAsync(Guid playerId, int itemId, int quantity, string reason, CancellationToken cancellationToken = default);

    /// <summary>Moves a stack, or part of it: into an empty slot it moves, onto the same item it merges, onto another item the whole stacks swap.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="fromSlot">Source slot.</param>
    /// <param name="toSlot">Target slot.</param>
    /// <param name="quantity">How many to move; zero or less means the whole stack.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the changed slots.</returns>
    Task<InventoryChange> MoveAsync(Guid playerId, int fromSlot, int toSlot, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Sorts an inventory: stacks of one item are merged, then laid out by item kind and id. Works for a player's inventory and for a chest's.</summary>
    /// <param name="ownerId">The inventory's owner: a player, or a chest (<c>ContainerIds.ForChest</c>).</param>
    /// <param name="firstSlot">The first slot the sort may touch; zero sorts the whole inventory, the hotbar size keeps the hotbar as it is.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the changed slots.</returns>
    Task<InventoryChange> SortAsync(Guid ownerId, int firstSlot, CancellationToken cancellationToken = default);

    /// <summary>Selects the hotbar slot the player holds.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the slot held afterwards.</returns>
    Task<(InventoryStatus Status, int SelectedSlot)> SelectSlotAsync(Guid playerId, int slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the item in the hotbar slot a player holds from memory, for the map snapshot: no database read and no command. Zero when the bag is not held in memory.
    /// </summary>
    /// <param name="ownerId">The player.</param>
    /// <returns>The item id, or zero for empty hands.</returns>
    int PeekHeldItemId(Guid ownerId);

    /// <summary>
    /// Grants the starter tools a player lacks, once each, with the audit reason <c>starter.backfill</c>. Accounts made before a tool joined the starter kit
    /// (axe, pickaxe, scythe) get it on their next join; a player who has every tool, or has no room, changes nothing, so calling it again is harmless.
    /// </summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the slots that changed (none when nothing was missing).</returns>
    Task<InventoryChange> BackfillStarterToolsAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Reads one slot.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="slot">Slot number.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The slot; an empty slot has item id zero.</returns>
    Task<InventorySlotView> PeekSlotAsync(Guid playerId, int slot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the deletion of a container's inventory (a chest being picked up or wiped with its land) in the caller's unit of work and drops the container from the
    /// in-memory copy, so the rows and the copy never disagree after the caller saves. This is the only way a container's rows are deleted outside this service.
    /// </summary>
    /// <param name="unit">The caller's unit of work; the deletion is saved with it.</param>
    /// <param name="containerId">The container's owner id (<c>ContainerIds</c>).</param>
    /// <param name="onlyIfEmpty">When <see langword="true"/> a container that holds items is left alone.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="false"/> when <paramref name="onlyIfEmpty"/> was set and the container holds items; otherwise <see langword="true"/>.</returns>
    Task<bool> DropContainerAsync(IUnitOfWork unit, Guid containerId, bool onlyIfEmpty, CancellationToken cancellationToken = default);

    /// <summary>Tells whether any of the containers holds an item.</summary>
    /// <param name="unit">The caller's unit of work, used for the containers that are not in memory.</param>
    /// <param name="containerIds">The containers' owner ids.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when at least one container holds an item.</returns>
    Task<bool> AnyContainerHoldsItemsAsync(IUnitOfWork unit, IReadOnlyCollection<Guid> containerIds, CancellationToken cancellationToken = default);

    /// <summary>Drops the in-memory copy of an owner's inventory (a player who signed out); the next call reloads it. The rows are not touched.</summary>
    /// <param name="ownerId">The player or container.</param>
    void Forget(Guid ownerId);
}

/// <summary>The operations to apply to one player's inventory inside <see cref="IInventoryService.ApplyManyAsync"/>.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="Operations">What to add and remove, applied in order; may be empty (the player's other data still joins the transaction).</param>
public readonly record struct PlayerInventoryChange(Guid PlayerId, IReadOnlyList<InventoryOperation> Operations);

/// <summary>One slot of an inventory.</summary>
/// <param name="Slot">Slot number, zero based.</param>
/// <param name="ItemId">Item id; zero for an empty slot.</param>
/// <param name="Quantity">How many are stacked; zero for an empty slot.</param>
public readonly record struct InventorySlotView(int Slot, int ItemId, int Quantity);

/// <summary>A player's inventory as read.</summary>
/// <param name="Capacity">Number of slots.</param>
/// <param name="SelectedSlot">The hotbar slot held.</param>
/// <param name="Slots">The occupied slots, ascending by slot number.</param>
public sealed record InventoryView(int Capacity, int SelectedSlot, IReadOnlyList<InventorySlotView> Slots);

/// <summary>Outcome of a change to an inventory.</summary>
/// <param name="Status">Ok, or why nothing changed.</param>
/// <param name="Changed">The slots that changed, in their new state; empty on a refusal.</param>
public readonly record struct InventoryChange(InventoryStatus Status, IReadOnlyList<InventorySlotView> Changed)
{
    /// <summary>Gets whether the change was applied.</summary>
    public bool IsOk => this.Status == InventoryStatus.Ok;
}

/// <summary>What kind of step an <see cref="InventoryOperation"/> is.</summary>
public enum InventoryOperationKind
{
    /// <summary>Adds items to stacks and empty slots.</summary>
    Add = 1,

    /// <summary>Removes items from any slots.</summary>
    Remove = 2,

    /// <summary>Removes items from one named slot, which must hold that item.</summary>
    RemoveFromSlot = 3,

    /// <summary>Adds items to one named slot, which must be empty or hold the same item with room for them.</summary>
    AddToSlot = 4,

    /// <summary>Grows the bag to a larger size (the quantity is the new capacity); the stacks stay where they are. Refused for a container, for a size that is not larger, and above the largest backpack.</summary>
    Grow = 5,
}

/// <summary>One step of an atomic inventory change.</summary>
/// <param name="Kind">The step.</param>
/// <param name="ItemId">Item id.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Slot">The slot, for <see cref="InventoryOperationKind.RemoveFromSlot"/>.</param>
public readonly record struct InventoryOperation(InventoryOperationKind Kind, int ItemId, int Quantity, int Slot = -1)
{
    /// <summary>Creates a step that adds items.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many.</param>
    /// <returns>The step.</returns>
    public static InventoryOperation Add(int itemId, int quantity) => new(InventoryOperationKind.Add, itemId, quantity);

    /// <summary>Creates a step that removes items from anywhere.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many.</param>
    /// <returns>The step.</returns>
    public static InventoryOperation Remove(int itemId, int quantity) => new(InventoryOperationKind.Remove, itemId, quantity);

    /// <summary>Creates a step that removes items from one slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="itemId">The item the slot must hold.</param>
    /// <param name="quantity">How many.</param>
    /// <returns>The step.</returns>
    public static InventoryOperation RemoveFromSlot(int slot, int itemId, int quantity) => new(InventoryOperationKind.RemoveFromSlot, itemId, quantity, slot);

    /// <summary>Creates a step that adds items to one slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many.</param>
    /// <returns>The step.</returns>
    public static InventoryOperation AddToSlot(int slot, int itemId, int quantity) => new(InventoryOperationKind.AddToSlot, itemId, quantity, slot);

    /// <summary>Creates a step that grows the inventory to <paramref name="capacity"/> slots (docs/adr/ADR-081).</summary>
    /// <param name="capacity">The new number of slots; it must be larger than the current one and no larger than <c>BackpackRules.MaxCapacity</c>.</param>
    /// <returns>The step.</returns>
    public static InventoryOperation Grow(int capacity) => new(InventoryOperationKind.Grow, 0, capacity);
}
