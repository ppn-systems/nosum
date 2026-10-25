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
using Nosum.Contracts.World.S2C;
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
/// An object belongs to the chunk of its first (top-left) tile: the instance keeps them in per-chunk buckets, a change is published to that chunk's subscribers and
/// the chunk's objects are sent once when the chunk joins a player's interest set (docs/adr/ADR-022).
/// </summary>
public sealed class PlaceableService : IPlaceableService, IInstanceWorld, IChunkStateContributor
{
    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly IChunkPublisher _publisher;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Task<InstanceState>> _instances = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _locks = new(32);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every action uses the server's own record of the player.</param>
    /// <param name="chunks">Who is interested in which chunk; a read returns only the objects in the player's interest set.</param>
    /// <param name="publisher">Delivers a change to the players who can see the chunk it happened in.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="inventory">Items are taken and returned through it.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">Clock for the audit timestamps.</param>
    public PlaceableService(IMapPresence presence, IChunkMap chunks, IChunkPublisher publisher, IMapCatalog maps, IInventoryService inventory, IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _chunks = chunks;
        _publisher = publisher;
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
            // Only what the player can see: the objects of the chunks in their interest set (all of them before the player has one).
            List<PlacedItem> visible = new(state.Set.Count);
            foreach (PlacedItem item in state.Set.Items)
            {
                if (player.Viewer is null || _chunks.IsInterested(player, ChunkCoord.OfTile(item.TileX, item.TileY)))
                {
                    visible.Add(item);
                }
            }

            return new PlacedSnapshotView(map.Id, visible);
        }
    }

    /// <inheritdoc/>
    public async ValueTask FillAsync(MapInstance instance, ChunkCoord chunk, ChunkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || !map.Placeable)
        {
            return;
        }

        InstanceState objects = await this.StateOfAsync(instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            if (objects.ByChunk.TryGetValue(chunk, out List<PlacedItem>? bucket))
            {
                foreach (PlacedItem item in bucket)
                {
                    state.Placed.Add(ToRow(item));
                }
            }
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

        await this.PushAsync(player.Instance, ChunkOf(placed), [placed], [], cancellationToken).ConfigureAwait(false);
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
        PlacedItem current;
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (!state.Set.TryGet(objectId, out current) || !PlaceableCatalog.TryGet(current.ItemId, out PlaceableDefinition? definition))
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

        // A move across a chunk border is a removal for the old chunk's subscribers and an upsert for the new one's (each sees only the half it holds). Removal first.
        if (ChunkOf(current) != ChunkOf(moved))
        {
            await this.PushAsync(player.Instance, ChunkOf(current), [], [objectId], cancellationToken).ConfigureAwait(false);
        }

        await this.PushAsync(player.Instance, ChunkOf(moved), [moved], [], cancellationToken).ConfigureAwait(false);
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
        PlacedItem current;
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (!state.Set.TryGet(objectId, out current) || !PlaceableCatalog.TryGet(current.ItemId, out PlaceableDefinition? definition))
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

        await this.PushAsync(player.Instance, ChunkOf(current), [], [objectId], cancellationToken).ConfigureAwait(false);
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

        return PlacedSet.CoversFeet(view, tileX, tileY, feetX, feetY, map.Grid.TileSize) ? PlaceStatus.PlayerInTheWay : PlaceStatus.Ok;
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

    // An object lives in the chunk of its first (top-left) tile.
    private static ChunkCoord ChunkOf(PlacedItem item) => ChunkCoord.OfTile(item.TileX, item.TileY);

    private async Task PushAsync(MapInstance instance, ChunkCoord chunk, IReadOnlyList<PlacedItem> upserted, IReadOnlyList<int> removed, CancellationToken cancellationToken)
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

        await _publisher.PublishAsync(instance, chunk, packet, null, cancellationToken).ConfigureAwait(false);
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

        // The objects of each chunk, for "everything in chunk (cx, cy)" without a scan of the room. Rebuilt with the grid, under the instance lock.
        public Dictionary<ChunkCoord, List<PlacedItem>> ByChunk { get; } = [];

        public void Rebuild(MapDefinition map)
        {
            Volatile.Write(ref this.Grid, this.Set.BuildGrid(map.Grid));
            this.ByChunk.Clear();
            foreach (PlacedItem item in this.Set.Items)
            {
                ChunkCoord chunk = ChunkOf(item);
                if (!this.ByChunk.TryGetValue(chunk, out List<PlacedItem>? bucket))
                {
                    bucket = [];
                    this.ByChunk[chunk] = bucket;
                }

                bucket.Add(item);
            }
        }
    }
}
