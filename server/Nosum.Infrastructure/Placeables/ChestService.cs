// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Farm;
using Nosum.Domain.Inventory;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// <see cref="IChestService"/>: a chest is one more inventory owner (<see cref="ContainerIds.ForChest"/>), so a move between the player and the chest is one
/// <see cref="IInventoryService.ApplyManyAsync"/>: both inventories are locked together, both change or neither does, and the chest's log line is saved in the same
/// transaction (docs/adr/ADR-024 D4). Two players moving the same stack race on the slot: the second finds it gone and is refused. Who may open a chest is
/// <see cref="PlacedAccess.MayUse"/>; the contents are sent when it is opened and then pushed to its viewers.
/// </summary>
public sealed class ChestService : IChestService, IPlacedBehaviour
{
    private const string DepositReason = "chest.deposit";
    private const string WithdrawReason = "chest.withdraw";

    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IPlaceableService _placeables;
    private readonly IInventoryService _inventory;
    private readonly ICoopMembership? _coops;
    private readonly TimeProvider _time;
    private readonly Dictionary<int, HashSet<Guid>> _viewers = [];
    private readonly Lock _gate = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every request uses the server's own record of the player.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="placeables">Finds the placed chest.</param>
    /// <param name="inventory">Moves the items.</param>
    /// <param name="coops">Co-op membership, asked on co-op land. <see langword="null"/> when there is no co-op land.</param>
    /// <param name="time">Clock for the audit timestamps.</param>
    public ChestService(IMapPresence presence, IMapCatalog maps, IPlaceableService placeables, IInventoryService inventory, ICoopMembership? coops, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _maps = maps;
        _placeables = placeables;
        _inventory = inventory;
        _coops = coops;
        _time = time;
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    PlaceableKind IPlacedBehaviour.Kind => PlaceableKind.Chest;

    /// <inheritdoc/>
    public async Task<ChestOpenOutcome> OpenAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (ChestStatus status, PlayerPresence? player) = await this.AuthorizeAsync(connection, objectId, cancellationToken).ConfigureAwait(false);
        if (status != ChestStatus.Ok || player is null)
        {
            return new ChestOpenOutcome(status, objectId, ChestRules.Capacity, []);
        }

        lock (_gate)
        {
            if (!_viewers.TryGetValue(objectId, out HashSet<Guid>? set))
            {
                set = [];
                _viewers[objectId] = set;
            }

            _ = set.Add(player.AccountId);
        }

        InventoryView view = await _inventory.GetAsync(ContainerIds.ForChest(objectId), cancellationToken).ConfigureAwait(false);
        return new ChestOpenOutcome(ChestStatus.Ok, objectId, view.Capacity, view.Slots);
    }

    /// <inheritdoc/>
    public ValueTask CloseAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (_presence.TryGet(connection, out PlayerPresence? player))
        {
            this.RemoveViewer(player.AccountId, objectId);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<ChestStatus> MoveAsync(IConnection connection, int objectId, ChestSide fromSide, int fromSlot, ChestSide toSide, int toSlot, int quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (ChestStatus status, PlayerPresence? player) = await this.AuthorizeAsync(connection, objectId, cancellationToken).ConfigureAwait(false);
        if (status != ChestStatus.Ok || player is null)
        {
            return status;
        }

        if (!this.IsViewer(player.AccountId, objectId))
        {
            return ChestStatus.NotOpen;
        }

        if (fromSide == ChestSide.Player && toSide == ChestSide.Player)
        {
            return ChestStatus.InvalidSlot;
        }

        Guid chestOwner = ContainerIds.ForChest(objectId);
        Guid source = fromSide == ChestSide.Chest ? chestOwner : player.AccountId;
        Guid target = toSide == ChestSide.Chest ? chestOwner : player.AccountId;

        ChestStatus result;
        if (fromSide == ChestSide.Chest && toSide == ChestSide.Chest)
        {
            // Inside the chest nothing is added or removed, so there is nothing to audit.
            result = ToStatus((await _inventory.MoveAsync(chestOwner, fromSlot, toSlot, quantity, cancellationToken).ConfigureAwait(false)).Status);
        }
        else
        {
            result = await this.MoveBetweenAsync(player, objectId, source, fromSlot, target, toSlot, quantity, fromSide, cancellationToken).ConfigureAwait(false);
        }

        if (result == ChestStatus.Ok)
        {
            await this.PushAsync(player.Instance, objectId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ChestStatus> SortAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (ChestStatus status, PlayerPresence? player) = await this.AuthorizeAsync(connection, objectId, cancellationToken).ConfigureAwait(false);
        if (status != ChestStatus.Ok || player is null)
        {
            return status;
        }

        if (!this.IsViewer(player.AccountId, objectId))
        {
            return ChestStatus.NotOpen;
        }

        // Inside the chest nothing is added or removed, so there is nothing to audit; the viewers see the new layout.
        ChestStatus result = ToStatus((await _inventory.SortAsync(ContainerIds.ForChest(objectId), 0, cancellationToken).ConfigureAwait(false)).Status);
        if (result == ChestStatus.Ok)
        {
            await this.PushAsync(player.Instance, objectId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc/>
    IReadOnlyList<Guid> IPlacedBehaviour.OwnersOf(PlacedItem item) => [ContainerIds.ForChest(item.Id)];

    /// <inheritdoc/>
    async Task<PlaceStatus> IPlacedBehaviour.JoinPickUpAsync(PlacedItem item, IUnitOfWork unit, CancellationToken cancellationToken)
    {
        // A chest that holds something is not picked up; an empty chest's inventory goes with it.
        return await _inventory.DropContainerAsync(unit, ContainerIds.ForChest(item.Id), onlyIfEmpty: true, cancellationToken).ConfigureAwait(false) ? PlaceStatus.Ok : PlaceStatus.NotEmpty;
    }

    /// <inheritdoc/>
    async ValueTask IPlacedBehaviour.PickedUpAsync(MapInstance instance, PlacedItem item, CancellationToken cancellationToken)
    {
        HashSet<Guid>? watchers;
        lock (_gate)
        {
            _ = _viewers.Remove(item.Id, out watchers);
        }

        if (watchers is null || watchers.Count == 0)
        {
            return;
        }

        using PacketScope<ChestClosed> lease = PacketFactory<ChestClosed>.Acquire();
        lease.Value.ObjectId = item.Id;
        List<PlayerPresence> here = [];
        _presence.CopyPlayers(instance.Key, here);
        foreach (PlayerPresence other in here)
        {
            if (watchers.Contains(other.AccountId))
            {
                await _presence.SendAsync(other.Connection, lease.Value, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // The same checks for every request: the player is somewhere with chests, the chest stands in their instance, they may use it and are within reach.
    private async Task<(ChestStatus Status, PlayerPresence? Player)> AuthorizeAsync(IConnection connection, int objectId, CancellationToken cancellationToken)
    {
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || !map.Placeable)
        {
            if (player is not null)
            {
                this.RemoveViewer(player.AccountId, objectId);
            }

            return (ChestStatus.UnknownObject, null);
        }

        PlacedItem? found = await _placeables.FindAsync(player.Instance, objectId, cancellationToken).ConfigureAwait(false);
        if (found is not { } chest || !PlaceableCatalog.TryGet(chest.ItemId, out PlaceableDefinition? definition) || definition.Kind != PlaceableKind.Chest)
        {
            this.RemoveViewer(player.AccountId, objectId);
            return (ChestStatus.UnknownObject, null);
        }

        bool member = false;
        if (map.IsCoop && _coops is not null)
        {
            CoopMembershipInfo? info = await _coops.FindAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
            member = info is not null && info.CoopId == player.Instance.OwnerId;
        }

        if (!PlacedAccess.MayUse(player.Instance, player.AccountId, member))
        {
            this.RemoveViewer(player.AccountId, objectId);
            return (ChestStatus.NotYours, null);
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        PlaceableView view = definition.View(Math.Min(chest.Rotation, definition.Rotations - 1));
        if (!PlacedAccess.Reaches(view, chest.TileX, chest.TileY, feetX, feetY, map.Grid.TileSize))
        {
            this.RemoveViewer(player.AccountId, objectId);
            return (ChestStatus.OutOfReach, null);
        }

        return (ChestStatus.Ok, player);
    }

    // A stack between the player and the chest: peek both slots, then one atomic change of both inventories plus the log line. The operations name their slots and
    // the quantities they expect, so a slot that changed since the peek (another player took the stack) refuses the whole change.
    private async Task<ChestStatus> MoveBetweenAsync(PlayerPresence player, int objectId, Guid source, int fromSlot, Guid target, int toSlot, int quantity, ChestSide fromSide, CancellationToken cancellationToken)
    {
        InventorySlotView from = await _inventory.PeekSlotAsync(source, fromSlot, cancellationToken).ConfigureAwait(false);
        if (from.ItemId == 0)
        {
            return ChestStatus.EmptySlot;
        }

        int count = quantity <= 0 ? from.Quantity : quantity;
        if (count > from.Quantity)
        {
            return ChestStatus.InvalidQuantity;
        }

        _ = ItemCatalog.TryGet(from.ItemId, out ItemDefinition? item);
        if (!item!.IsTradable && fromSide == ChestSide.Player)
        {
            return ChestStatus.NotAllowed;
        }

        List<InventoryOperation> sourceOps = [];
        List<InventoryOperation> targetOps = [];
        InventorySlotView? displaced = null;
        if (toSlot < 0)
        {
            sourceOps.Add(InventoryOperation.RemoveFromSlot(fromSlot, from.ItemId, count));
            targetOps.Add(InventoryOperation.Add(from.ItemId, count));
        }
        else
        {
            InventorySlotView into = await _inventory.PeekSlotAsync(target, toSlot, cancellationToken).ConfigureAwait(false);
            if (into.ItemId == 0)
            {
                sourceOps.Add(InventoryOperation.RemoveFromSlot(fromSlot, from.ItemId, count));
                targetOps.Add(InventoryOperation.AddToSlot(toSlot, from.ItemId, count));
            }
            else if (into.ItemId == from.ItemId)
            {
                int room = item.MaxStack - into.Quantity;
                if (room <= 0)
                {
                    return ChestStatus.NoSpace;
                }

                count = Math.Min(room, count);
                sourceOps.Add(InventoryOperation.RemoveFromSlot(fromSlot, from.ItemId, count));
                targetOps.Add(InventoryOperation.AddToSlot(toSlot, from.ItemId, count));
            }
            else
            {
                // Two different stacks swap as wholes; a part of a stack cannot swap with another item.
                if (count != from.Quantity)
                {
                    return ChestStatus.Blocked;
                }

                _ = ItemCatalog.TryGet(into.ItemId, out ItemDefinition? other);
                if (!other!.IsTradable && fromSide == ChestSide.Chest)
                {
                    return ChestStatus.NotAllowed;
                }

                displaced = into;
                sourceOps.Add(InventoryOperation.RemoveFromSlot(fromSlot, from.ItemId, count));
                sourceOps.Add(InventoryOperation.AddToSlot(fromSlot, into.ItemId, into.Quantity));
                targetOps.Add(InventoryOperation.RemoveFromSlot(toSlot, into.ItemId, into.Quantity));
                targetOps.Add(InventoryOperation.AddToSlot(toSlot, from.ItemId, count));
            }
        }

        // The chest's net change per item, for the log: what went in is a deposit, what came out a withdrawal.
        int movedIn = fromSide == ChestSide.Player ? count : -count;
        int displacedIn = fromSide == ChestSide.Player ? -(displaced?.Quantity ?? 0) : (displaced?.Quantity ?? 0);
        InventoryStatus status = await _inventory.ApplyManyAsync(
            [new PlayerInventoryChange(source, sourceOps), new PlayerInventoryChange(target, targetOps)],
            movedIn > 0 ? DepositReason : WithdrawReason,
            async (unit, ct) =>
            {
                // The chest may have been picked up between the check and now; its row is the truth, read under the chest's lock.
                if (await unit.Repository<PlacedObject>().FindAsync(objectId, ct).ConfigureAwait(false) is null)
                {
                    return false;
                }

                this.Log(unit, objectId, player.AccountId, from.ItemId, movedIn);
                if (displaced is { } other)
                {
                    this.Log(unit, objectId, player.AccountId, other.ItemId, displacedIn);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);
        return ToStatus(status);
    }

    private void Log(IUnitOfWork unit, int objectId, Guid playerId, int itemId, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        unit.Repository<ChestLogEntry>().Add(new ChestLogEntry
        {
            ObjectId = objectId,
            PlayerId = playerId,
            ItemId = itemId,
            Delta = delta,
            Reason = delta > 0 ? DepositReason : WithdrawReason,
            OccurredAt = _time.GetUtcNow(),
        });
    }

    // Every viewer still in the instance gets the chest's slots, each in its new state (the whole chest: at most 36 small rows).
    private async Task PushAsync(MapInstance instance, int objectId, CancellationToken cancellationToken)
    {
        HashSet<Guid> watchers;
        lock (_gate)
        {
            if (!_viewers.TryGetValue(objectId, out HashSet<Guid>? set) || set.Count == 0)
            {
                return;
            }

            watchers = [.. set];
        }

        InventoryView view = await _inventory.GetAsync(ContainerIds.ForChest(objectId), cancellationToken).ConfigureAwait(false);
        using PacketScope<ChestUpdate> lease = PacketFactory<ChestUpdate>.Acquire();
        ChestUpdate packet = lease.Value;
        packet.ObjectId = objectId;
        packet.Slots.Clear();
        bool[] occupied = new bool[view.Capacity];
        foreach (InventorySlotView slot in view.Slots)
        {
            occupied[slot.Slot] = true;
            packet.Slots.Add(RowOf(slot.Slot, slot.ItemId, slot.Quantity));
        }

        for (int i = 0; i < occupied.Length; i++)
        {
            if (!occupied[i])
            {
                packet.Slots.Add(RowOf(i, 0, 0));
            }
        }

        List<PlayerPresence> here = [];
        _presence.CopyPlayers(instance.Key, here);
        foreach (PlayerPresence other in here)
        {
            if (watchers.Contains(other.AccountId))
            {
                await _presence.SendAsync(other.Connection, packet, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static InventorySlotRow RowOf(int slot, int itemId, int quantity)
    {
        InventorySlotRow row = InventorySlotRow.Create();
        row.Slot = (byte)slot;
        row.ItemId = itemId;
        row.Quantity = quantity;
        return row;
    }

    private bool IsViewer(Guid accountId, int objectId)
    {
        lock (_gate)
        {
            return _viewers.TryGetValue(objectId, out HashSet<Guid>? set) && set.Contains(accountId);
        }
    }

    private void RemoveViewer(Guid accountId, int objectId)
    {
        lock (_gate)
        {
            if (_viewers.TryGetValue(objectId, out HashSet<Guid>? set) && set.Remove(accountId) && set.Count == 0)
            {
                _ = _viewers.Remove(objectId);
            }
        }
    }

    // A player who left the place (a map change, a disconnect) no longer watches any chest.
    private ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        lock (_gate)
        {
            List<int> emptied = [];
            foreach ((int objectId, HashSet<Guid> set) in _viewers)
            {
                if (set.Remove(player.AccountId) && set.Count == 0)
                {
                    emptied.Add(objectId);
                }
            }

            foreach (int objectId in emptied)
            {
                _ = _viewers.Remove(objectId);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static ChestStatus ToStatus(InventoryStatus status) => status switch
    {
        InventoryStatus.Ok => ChestStatus.Ok,
        InventoryStatus.InvalidSlot => ChestStatus.InvalidSlot,
        InventoryStatus.EmptySlot or InventoryStatus.NotEnough => ChestStatus.EmptySlot,
        InventoryStatus.InvalidQuantity => ChestStatus.InvalidQuantity,
        InventoryStatus.NoSpace => ChestStatus.NoSpace,
        InventoryStatus.Blocked => ChestStatus.Blocked,
        _ => ChestStatus.Conflict,
    };
}
