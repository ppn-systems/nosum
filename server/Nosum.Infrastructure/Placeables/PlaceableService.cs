// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// <see cref="IPlaceableService"/> and <see cref="IInstanceWorld"/>: the objects players placed in their own place.
/// Every rule is <see cref="PlacedSet.Check"/> plus the server's own record of the player (position, instance, inventory slot);
/// the placed set of an instance is loaded once, kept in memory and written through to PostgreSQL in the same transaction as the
/// inventory change, so an item is never both in the inventory and in the room (docs/adr/ADR-015). One lock per instance serializes changes.
/// </summary>
public sealed class PlaceableService : IPlaceableService, IInstanceWorld
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Task<InstanceState>> _instances = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _locks = new(32);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every action uses the server's own record of the player.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="inventory">Items are taken and returned through it.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">Clock for the audit timestamps.</param>
    public PlaceableService(IMapPresence presence, IMapCatalog maps, IInventoryService inventory, IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default)
    {
        if (_maps.TryGet(instance.MapId, out MapDefinition? map) && map.Placeable)
        {
            _ = await this.StateOfAsync(instance, map, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public WalkGrid GridFor(MapInstance instance, MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return map.Placeable && _instances.TryGetValue(instance.Key, out Task<InstanceState>? load) && load.IsCompletedSuccessfully
            ? Volatile.Read(ref load.Result.Grid)
            : map.Grid;
    }

    /// <inheritdoc/>
    public async Task<PlacedSnapshotView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || !map.Placeable)
        {
            return new PlacedSnapshotView(string.Empty, []);
        }

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            return new PlacedSnapshotView(map.Id, [.. state.Set.Items]);
        }
    }

    /// <inheritdoc/>
    public async Task<PlaceOutcome> PlaceAsync(IConnection connection, int slot, int tileX, int tileY, int rotation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryEditablePlace(connection, out PlayerPresence? player, out MapDefinition? map))
        {
            return Refused(PlaceStatus.NotPlaceable);
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
        if (held.ItemId == 0 || held.Quantity < 1 || !PlaceableCatalog.TryGet(held.ItemId, out PlaceableDefinition? definition))
        {
            return Refused(PlaceStatus.NoItem);
        }

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        PlacedObject row;
        PlacedItem placed;
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (state.Set.Count >= PlacementLimits.MaxPerInstance)
            {
                return Refused(PlaceStatus.LimitReached);
            }

            PlaceStatus status = Validate(state, map, player, definition, rotation, tileX, tileY, ignoreId: 0);
            if (status != PlaceStatus.Ok)
            {
                return Refused(status);
            }

            DateTimeOffset now = _time.GetUtcNow();
            row = new PlacedObject { MapId = player.Instance.MapId, OwnerId = player.Instance.OwnerId, ItemId = held.ItemId, TileX = tileX, TileY = tileY, Rotation = rotation, PlacedAt = now, UpdatedAt = now };
            InventoryChange change = await _inventory.ApplyAsync(
                player.AccountId,
                [InventoryOperation.RemoveFromSlot(slot, held.ItemId, 1)],
                "placed.place",
                (unit, _) =>
                {
                    unit.Repository<PlacedObject>().Add(row);
                    return Task.FromResult(true);
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return Refused(ToStatus(change.Status));
            }

            placed = new PlacedItem(row.Id, row.ItemId, tileX, tileY, rotation);
            state.Set.Add(placed);
            state.Rebuild(map);
        }

        await this.PushAsync(player.Instance, [placed], [], cancellationToken).ConfigureAwait(false);
        return new PlaceOutcome(PlaceStatus.Ok, placed.Id);
    }

    /// <inheritdoc/>
    public async Task<PlaceOutcome> MoveAsync(IConnection connection, int objectId, int tileX, int tileY, int rotation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryEditablePlace(connection, out PlayerPresence? player, out MapDefinition? map))
        {
            return Refused(PlaceStatus.NotPlaceable);
        }

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        PlacedItem moved;
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (!state.Set.TryGet(objectId, out PlacedItem current) || !PlaceableCatalog.TryGet(current.ItemId, out PlaceableDefinition? definition))
            {
                return Refused(PlaceStatus.UnknownObject);
            }

            PlaceStatus status = Validate(state, map, player, definition, rotation, tileX, tileY, ignoreId: objectId);
            if (status != PlaceStatus.Ok)
            {
                return Refused(status);
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                PlacedObject? row = await unit.Repository<PlacedObject>().FindAsync(objectId, cancellationToken).ConfigureAwait(false);
                if (row is null)
                {
                    return Refused(PlaceStatus.UnknownObject);
                }

                row.TileX = tileX;
                row.TileY = tileY;
                row.Rotation = rotation;
                row.UpdatedAt = _time.GetUtcNow();
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            moved = current with { TileX = tileX, TileY = tileY, Rotation = rotation };
            _ = state.Set.Replace(moved);
            state.Rebuild(map);
        }

        await this.PushAsync(player.Instance, [moved], [], cancellationToken).ConfigureAwait(false);
        return new PlaceOutcome(PlaceStatus.Ok, objectId);
    }

    /// <inheritdoc/>
    public async Task<PlaceOutcome> PickUpAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryEditablePlace(connection, out PlayerPresence? player, out MapDefinition? map))
        {
            return Refused(PlaceStatus.NotPlaceable);
        }

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (!state.Set.TryGet(objectId, out PlacedItem current) || !PlaceableCatalog.TryGet(current.ItemId, out PlaceableDefinition? definition))
            {
                return Refused(PlaceStatus.UnknownObject);
            }

            PlaceableView view = definition.View(Math.Min(current.Rotation, definition.Rotations - 1));
            if (!Reaches(player, map, current.TileX, current.TileY, view))
            {
                return Refused(PlaceStatus.OutOfReach);
            }

            InventoryChange change = await _inventory.ApplyAsync(
                player.AccountId,
                [InventoryOperation.Add(current.ItemId, 1)],
                "placed.pickup",
                async (unit, ct) =>
                {
                    PlacedObject? row = await unit.Repository<PlacedObject>().FindAsync(objectId, ct).ConfigureAwait(false);
                    if (row is null)
                    {
                        return false;
                    }

                    unit.Repository<PlacedObject>().Remove(row);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return Refused(ToStatus(change.Status));
            }

            _ = state.Set.Remove(objectId);
            state.Rebuild(map);
        }

        await this.PushAsync(player.Instance, [], [objectId], cancellationToken).ConfigureAwait(false);
        return new PlaceOutcome(PlaceStatus.Ok, objectId);
    }

    private static PlaceOutcome Refused(PlaceStatus status) => new(status, 0);

    private static PlaceStatus ToStatus(InventoryStatus status) => status switch
    {
        InventoryStatus.Ok => PlaceStatus.Ok,
        InventoryStatus.NoSpace => PlaceStatus.InventoryFull,
        InventoryStatus.Conflict => PlaceStatus.Conflict,
        _ => PlaceStatus.NoItem,
    };

    private static PlaceStatus ToStatus(PlacementVerdict verdict) => verdict switch
    {
        PlacementVerdict.Ok => PlaceStatus.Ok,
        PlacementVerdict.BadRotation => PlaceStatus.BadRotation,
        PlacementVerdict.OutsideRoom => PlaceStatus.OutsideRoom,
        PlacementVerdict.Blocked => PlaceStatus.Blocked,
        PlacementVerdict.Reserved => PlaceStatus.Reserved,
        _ => PlaceStatus.Overlaps,
    };

    // The footprint must be free, within the player's reach, and not on the player's own feet.
    private static PlaceStatus Validate(InstanceState state, MapDefinition map, PlayerPresence player, PlaceableDefinition definition, int rotation, int tileX, int tileY, int ignoreId)
    {
        PlacementVerdict verdict = state.Set.Check(definition, rotation, tileX, tileY, map.Grid, map.ReservedTiles, ignoreId);
        if (verdict != PlacementVerdict.Ok)
        {
            return ToStatus(verdict);
        }

        PlaceableView view = definition.View(rotation);
        if (!Reaches(player, map, tileX, tileY, view))
        {
            return PlaceStatus.OutOfReach;
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        // The grid with the new footprint; a piece being moved no longer stands where it was.
        PlacedSet remaining = new();
        foreach (PlacedItem item in state.Set.Items)
        {
            if (item.Id != ignoreId)
            {
                remaining.Add(item);
            }
        }

        WalkGrid candidate = remaining.BuildGrid(map.Grid);
        candidate.Block(tileX, tileY, view.FootprintWidth, view.FootprintHeight);
        return MoveValidator.FootBlocked(candidate, feetX, feetY) && !MoveValidator.FootBlocked(state.Grid, feetX, feetY)
            ? PlaceStatus.PlayerInTheWay
            : PlaceStatus.Ok;
    }

    // Within the farming reach of any tile of the footprint.
    private static bool Reaches(PlayerPresence player, MapDefinition map, int tileX, int tileY, PlaceableView view)
    {
        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        for (int y = tileY; y < tileY + view.FootprintHeight; y++)
        {
            for (int x = tileX; x < tileX + view.FootprintWidth; x++)
            {
                if (FarmRules.InReach(feetX, feetY, x, y, map.Grid.TileSize))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Objects are changed only by the owner standing in their own place on a map that allows it.
    private bool TryEditablePlace(IConnection connection, out PlayerPresence player, out MapDefinition map)
    {
        if (_presence.TryGet(connection, out PlayerPresence? found)
            && _maps.TryGet(found.MapId, out MapDefinition? foundMap)
            && foundMap.Placeable
            && found.Instance.IsPrivate
            && found.Instance.OwnerId == found.AccountId)
        {
            player = found;
            map = foundMap;
            return true;
        }

        player = null!;
        map = null!;
        return false;
    }

    // Loads an instance's objects once; a failed load is forgotten so the next call tries again.
    private async Task<InstanceState> StateOfAsync(MapInstance instance, MapDefinition map, CancellationToken cancellationToken)
    {
        string key = instance.Key;
        Task<InstanceState> load = _instances.GetOrAdd(key, _ => this.LoadAsync(instance, map));
        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (load.IsFaulted)
        {
            _ = _instances.TryRemove(new KeyValuePair<string, Task<InstanceState>>(key, load));
            throw;
        }
    }

    private async Task<InstanceState> LoadAsync(MapInstance instance, MapDefinition map)
    {
        InstanceState state = new(map);
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<PlacedObject> rows = await unit.Repository<PlacedObject>().ListAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
            foreach (PlacedObject row in rows)
            {
                state.Set.Add(new PlacedItem(row.Id, row.ItemId, row.TileX, row.TileY, row.Rotation));
            }
        }

        state.Rebuild(map);
        return state;
    }

    private async Task PushAsync(MapInstance instance, IReadOnlyList<PlacedItem> upserted, IReadOnlyList<int> removed, CancellationToken cancellationToken)
    {
        using PacketScope<PlacedChanged> lease = PacketFactory<PlacedChanged>.Acquire();
        PlacedChanged packet = lease.Value;
        packet.MapId = instance.MapId;
        packet.Upserted.Clear();
        packet.Removed.Clear();
        foreach (PlacedItem item in upserted)
        {
            packet.Upserted.Add(ToRow(item));
        }

        foreach (int id in removed)
        {
            packet.Removed.Add(id);
        }

        await _presence.MulticastAsync(instance.Key, packet, null, cancellationToken).ConfigureAwait(false);
    }

    private static PlacedObjectRow ToRow(PlacedItem item)
    {
        PlacedObjectRow row = PlacedObjectRow.Create();
        row.Id = item.Id;
        row.ItemId = item.ItemId;
        row.TileX = (ushort)item.TileX;
        row.TileY = (ushort)item.TileY;
        row.Rotation = (byte)item.Rotation;
        return row;
    }

    private sealed class InstanceState
    {
        public InstanceState(MapDefinition map) => this.Grid = map.Grid;

        public PlacedSet Set { get; } = new();

        // Replaced as a whole under the instance lock; movement validation reads it without one.
        public WalkGrid Grid;

        public void Rebuild(MapDefinition map) => Volatile.Write(ref this.Grid, this.Set.BuildGrid(map.Grid));
    }
}
