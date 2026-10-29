// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Coops;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Visits;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// <see cref="IPlaceableService"/> and <see cref="IInstanceWorld"/>: the objects players placed in their own place (furniture in the farmhouse, crafted objects on the farm and co-op land).
/// Every rule is <see cref="PlacedSet.Check"/> plus the server's own record of the player (position, instance, inventory slot);
/// the placed set of an instance is loaded once, kept in memory and written through to PostgreSQL in the same transaction as the
/// inventory change, so an item is never both in the inventory and in the room (docs/adr/ADR-015). One lock per instance serializes changes.
/// An object belongs to every chunk its footprint overlaps (<see cref="PlacedChunks"/>): the instance keeps them in per-chunk buckets, a change is published to each of
/// those chunks' subscribers and a chunk's objects are sent once when the chunk joins a player's interest set (docs/adr/ADR-022).
/// </summary>
public sealed partial class PlaceableService : IPlaceableService, IInstanceWorld, IChunkStateContributor, IInstanceLock
{
    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly IChunkPublisher _publisher;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IFarmTiles? _farm;
    private readonly INodeTiles? _nodes;
    private readonly ICoopMembership? _coops;
    private readonly PlacedTiles? _tiles;
    private readonly List<IPlacedBehaviour> _behaviours = [];
    private readonly ConcurrentDictionary<string, Task<InstanceState>> _instances = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _locks;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every action uses the server's own record of the player.</param>
    /// <param name="chunks">Who is interested in which chunk; a read returns only the objects in the player's interest set.</param>
    /// <param name="publisher">Delivers a change to the players who can see the chunk it happened in.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="inventory">Items are taken and returned through it.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">Clock for the audit timestamps.</param>
    /// <param name="farm">Which tiles are tilled: nothing is placed on worked soil (docs/adr/ADR-024 D3). <see langword="null"/> to skip the check.</param>
    /// <param name="nodes">Which tiles hold a node: nothing is placed on a tree or a weed. <see langword="null"/> to skip the check.</param>
    /// <param name="coops">Co-op membership, asked for every change on co-op land. <see langword="null"/> when there is no co-op land.</param>
    /// <param name="tiles">Where the pieces of every loaded instance are published for the farm layer and the growth rule.</param>
    public PlaceableService(
        IMapPresence presence,
        IChunkMap chunks,
        IChunkPublisher publisher,
        IMapCatalog maps,
        IInventoryService inventory,
        IUnitOfWorkFactory unitOfWork,
        TimeProvider time,
        IFarmTiles? farm = null,
        INodeTiles? nodes = null,
        ICoopMembership? coops = null,
        PlacedTiles? tiles = null)
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
        _farm = farm;
        _nodes = nodes;
        _coops = coops;
        _tiles = tiles;
        _locks = new StripedAsyncLock(32);
    }

    /// <inheritdoc/>
    public async ValueTask<IDisposable> EnterAsync(MapInstance instance, CancellationToken cancellationToken = default)
        => await _locks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false);

    /// <summary>Adds what a kind of object does beyond standing there (a chest's contents, a furnace's job); call once per behaviour while building the host.</summary>
    /// <param name="behaviour">The behaviour.</param>
    public void Attach(IPlacedBehaviour behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        _behaviours.Add(behaviour);
    }

    /// <inheritdoc/>
    public async Task<PlacedItem?> FindAsync(MapInstance instance, int objectId, CancellationToken cancellationToken = default)
    {
        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || !map.Placeable)
        {
            return null;
        }

        InstanceState state = await this.StateOfAsync(instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            return state.Set.TryGet(objectId, out PlacedItem found) ? found : null;
        }
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
            // Only what the player can see: the objects that stand in a chunk of their interest set (all of them before the player has one).
            List<PlacedItem> visible = new(state.Set.Count);
            foreach (PlacedItem item in state.Set.Items)
            {
                if (player.Viewer is null || this.AnyInterested(player, PlacedChunks.RangeOf(item)))
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

        PlaceStatus editable = this.TryEditablePlace(connection, PlaceAction.PlaceObject, out PlayerPresence? player, out MapDefinition? map);
        if (editable != PlaceStatus.Ok)
        {
            return Refused(editable);
        }

        CoopMembershipInfo? member = await this.MemberOnCoopLandAsync(player, map, cancellationToken).ConfigureAwait(false);
        if (map.IsCoop && member is null)
        {
            return Refused(PlaceStatus.NotYourPlace);
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
        if (held.ItemId == 0 || held.Quantity < 1 || !PlaceableCatalog.TryGet(held.ItemId, out PlaceableDefinition? definition))
        {
            return Refused(PlaceStatus.NoItem);
        }

        // A building stands on the farm or on co-op land, never in the farmhouse (docs/adr/ADR-028 D1).
        if (definition.Kind == PlaceableKind.Building && !(GameMaps.TryGet(map.Id, out MapSpec? spec) && spec.Farmable))
        {
            return Refused(PlaceStatus.NotPlaceable);
        }

        if (member is not null && !FootprintUnlocked(member, definition, rotation, tileX, tileY))
        {
            return Refused(PlaceStatus.LockedLand);
        }

        IGroundLayers? layers = await this.LayersAsync(player.Instance, definition, rotation, tileX, tileY, cancellationToken).ConfigureAwait(false);

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        PlacedObject row;
        PlacedItem placed;
        IPlacedBehaviour? behaviour = _behaviours.Find(b => b.Kind == definition.Kind);
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (state.Set.Count >= PlacementLimits.MaxPerInstance || (definition.Kind == PlaceableKind.Building && CountBuildings(state.Set) >= BuildingRules.MaxPerLand))
            {
                return Refused(PlaceStatus.LimitReached);
            }

            PlaceStatus status = this.Validate(state, map, player, definition, rotation, tileX, tileY, ignoreId: 0, layers);
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
                    behaviour?.JoinPlace(row, unit, now);
                    this.Audit(unit, member, held.ItemId, -1, CoopLand.PlaceReason);
                    return Task.FromResult(true);
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return Refused(ToStatus(change.Status));
            }

            placed = new PlacedItem(row.Id, row.ItemId, tileX, tileY, rotation);
            state.Set.Add(placed);
            this.Rebuild(state, player.Instance, map);
        }

        if (behaviour is not null)
        {
            await behaviour.PlacedAsync(player.Instance, placed, cancellationToken).ConfigureAwait(false);
        }

        await this.PushAsync(player.Instance, PlacedChunks.RangeOf(placed).Chunks(), [placed], [], cancellationToken).ConfigureAwait(false);
        return new PlaceOutcome(PlaceStatus.Ok, placed.Id);
    }

    /// <inheritdoc/>
    public async Task<PlaceOutcome> MoveAsync(IConnection connection, int objectId, int tileX, int tileY, int rotation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        PlaceStatus editable = this.TryEditablePlace(connection, PlaceAction.MoveObject, out PlayerPresence? player, out MapDefinition? map);
        if (editable != PlaceStatus.Ok)
        {
            return Refused(editable);
        }

        CoopMembershipInfo? mover = await this.MemberOnCoopLandAsync(player, map, cancellationToken).ConfigureAwait(false);
        if (map.IsCoop && mover is null)
        {
            return Refused(PlaceStatus.NotYourPlace);
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

            if (definition.Kind == PlaceableKind.Building)
            {
                return Refused(PlaceStatus.Fixed);
            }

            PlaceableView currentView = definition.View(Math.Min(current.Rotation, definition.Rotations - 1));
            if (!Reaches(player, map, current.TileX, current.TileY, currentView))
            {
                return Refused(PlaceStatus.OutOfReach);
            }

            if (mover is not null && !FootprintUnlocked(mover, definition, rotation, tileX, tileY))
            {
                return Refused(PlaceStatus.LockedLand);
            }

            IGroundLayers? layers = await this.LayersAsync(player.Instance, definition, rotation, tileX, tileY, cancellationToken).ConfigureAwait(false);
            PlaceStatus status = this.Validate(state, map, player, definition, rotation, tileX, tileY, ignoreId: objectId, layers);
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
            this.Rebuild(state, player.Instance, map);
        }

        // A move removes the piece from the chunks it left and upserts it in every chunk it now overlaps. Removal first.
        ChunkRange before = PlacedChunks.RangeOf(current);
        ChunkRange after = PlacedChunks.RangeOf(moved);
        List<ChunkCoord> left = [];
        foreach (ChunkCoord chunk in before.Chunks())
        {
            if (!after.Contains(chunk))
            {
                left.Add(chunk);
            }
        }

        await this.PushAsync(player.Instance, left, [], [objectId], cancellationToken).ConfigureAwait(false);
        await this.PushAsync(player.Instance, after.Chunks(), [moved], [], cancellationToken).ConfigureAwait(false);
        return new PlaceOutcome(PlaceStatus.Ok, objectId);
    }

    /// <inheritdoc/>
    public async Task<PlaceOutcome> PickUpAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        PlaceStatus editable = this.TryEditablePlace(connection, PlaceAction.PickUpObject, out PlayerPresence? player, out MapDefinition? map);
        if (editable != PlaceStatus.Ok)
        {
            return Refused(editable);
        }

        CoopMembershipInfo? member = await this.MemberOnCoopLandAsync(player, map, cancellationToken).ConfigureAwait(false);
        if (map.IsCoop && member is null)
        {
            return Refused(PlaceStatus.NotYourPlace);
        }

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        PlacedItem current;
        IPlacedBehaviour? behaviour;
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (!state.Set.TryGet(objectId, out current) || !PlaceableCatalog.TryGet(current.ItemId, out PlaceableDefinition? definition))
            {
                return Refused(PlaceStatus.UnknownObject);
            }

            if (definition.Kind == PlaceableKind.Building)
            {
                return Refused(PlaceStatus.Fixed);
            }

            PlaceableView view = definition.View(Math.Min(current.Rotation, definition.Rotations - 1));
            if (!Reaches(player, map, current.TileX, current.TileY, view))
            {
                return Refused(PlaceStatus.OutOfReach);
            }

            // A chest and a furnace join the transaction: they refuse while they hold something, and the chest's own inventory is locked with the player's.
            behaviour = _behaviours.Find(b => b.Kind == definition.Kind);
            List<PlayerInventoryChange> changes = [new PlayerInventoryChange(player.AccountId, [InventoryOperation.Add(current.ItemId, 1)])];
            if (behaviour is not null)
            {
                foreach (Guid owner in behaviour.OwnersOf(current))
                {
                    changes.Add(new PlayerInventoryChange(owner, []));
                }
            }

            PlaceStatus refusal = PlaceStatus.Ok;
            InventoryStatus status = await _inventory.ApplyManyAsync(
                changes,
                "placed.pickup",
                async (unit, ct) =>
                {
                    PlacedObject? row = await unit.Repository<PlacedObject>().FindAsync(objectId, ct).ConfigureAwait(false);
                    if (row is null)
                    {
                        refusal = PlaceStatus.UnknownObject;
                        return false;
                    }

                    if (behaviour is not null)
                    {
                        refusal = await behaviour.JoinPickUpAsync(current, unit, ct).ConfigureAwait(false);
                        if (refusal != PlaceStatus.Ok)
                        {
                            return false;
                        }
                    }

                    unit.Repository<PlacedObject>().Remove(row);
                    this.Audit(unit, member, current.ItemId, 1, CoopLand.PickUpReason);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (status != InventoryStatus.Ok)
            {
                return Refused(refusal != PlaceStatus.Ok ? refusal : ToStatus(status));
            }

            _ = state.Set.Remove(objectId);
            this.Rebuild(state, player.Instance, map);
        }

        if (behaviour is not null)
        {
            await behaviour.PickedUpAsync(player.Instance, current, cancellationToken).ConfigureAwait(false);
        }

        await this.PushAsync(player.Instance, PlacedChunks.RangeOf(current).Chunks(), [], [objectId], cancellationToken).ConfigureAwait(false);
        return new PlaceOutcome(PlaceStatus.Ok, objectId);
    }

    private static PlaceOutcome Refused(PlaceStatus status) => new(status, 0);

    // How many farm buildings an instance holds: a land takes at most BuildingRules.MaxPerLand.
    private static int CountBuildings(PlacedSet set)
    {
        int count = 0;
        foreach (PlacedItem item in set.Items)
        {
            if (PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition) && definition.Kind == PlaceableKind.Building)
            {
                count++;
            }
        }

        return count;
    }

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
        PlacementVerdict.Tilled => PlaceStatus.Tilled,
        PlacementVerdict.Node => PlaceStatus.NodeInTheWay,
        _ => PlaceStatus.Overlaps,
    };

    // The footprint must be free and within the player's reach; a piece that blocks walking is also not on any present player's feet.
    private PlaceStatus Validate(InstanceState state, MapDefinition map, PlayerPresence player, PlaceableDefinition definition, int rotation, int tileX, int tileY, int ignoreId, IGroundLayers? layers)
    {
        PlacementVerdict verdict = state.Set.Check(definition, rotation, tileX, tileY, map.Grid, map.ReservedTiles, ignoreId, layers);
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

        // A piece that does not block walking (a path) may go under anyone's feet, the placer's included; only a blocking one must stand clear.
        if (definition.BlocksWalking)
        {
            if (PlacedSet.CoversFeet(view, tileX, tileY, feetX, feetY, map.Grid.TileSize))
            {
                return PlaceStatus.PlayerInTheWay;
            }

            List<PlayerPresence> present = [];
            _presence.CopyPlayers(player.Key, present);
            foreach (PlayerPresence other in present)
            {
                if (other.AccountId == player.AccountId)
                {
                    continue;
                }

                double otherX;
                double otherY;
                lock (other.Gate)
                {
                    otherX = other.Motion.X;
                    otherY = other.Motion.Y;
                }

                if (PlacedSet.CoversFeet(view, tileX, tileY, otherX, otherY, map.Grid.TileSize))
                {
                    return PlaceStatus.PlayerInTheWay;
                }
            }
        }

        return PlaceStatus.Ok;
    }

    // Whether the whole footprint lies on land the co-op has unlocked (docs/adr/ADR-038); a rotation out of range is left to the placement check.
    private static bool FootprintUnlocked(CoopMembershipInfo member, PlaceableDefinition definition, int rotation, int tileX, int tileY)
    {
        if ((uint)rotation >= (uint)definition.Rotations)
        {
            return true;
        }

        PlaceableView view = definition.View(rotation);
        return CoopUpgrades.IsAreaUnlocked(member.LandLevel, tileX, tileY, view.FootprintWidth, view.FootprintHeight);
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

        return PlacedAccess.Reaches(view, tileX, tileY, feetX, feetY, map.Grid.TileSize);
    }

    // The tilled soil and the nodes under a footprint, looked up before the instance lock (they are other services' state); the verdict uses them as plain sets.
    private async Task<IGroundLayers?> LayersAsync(MapInstance instance, PlaceableDefinition definition, int rotation, int tileX, int tileY, CancellationToken cancellationToken)
    {
        if ((uint)rotation >= (uint)definition.Rotations)
        {
            return null;
        }

        PlaceableView view = definition.View(rotation);
        IReadOnlySet<(int X, int Y)> tilled = _farm is null
            ? new HashSet<(int X, int Y)>()
            : await _farm.WorkedTilesAsync(instance, tileX, tileY, view.FootprintWidth, view.FootprintHeight, cancellationToken).ConfigureAwait(false);

        // A building's door needs the tile in front of it free of nodes too, one row below the footprint (docs/adr/ADR-028 D1).
        int nodeRows = view.FootprintHeight + (definition.Door is null ? 0 : 1);
        IReadOnlySet<(int X, int Y)> nodes = _nodes is null
            ? new HashSet<(int X, int Y)>()
            : await _nodes.NodeTilesAsync(instance, tileX, tileY, view.FootprintWidth, nodeRows, cancellationToken).ConfigureAwait(false);
        return new GroundLayers(tilled, nodes);
    }

    // The co-op membership of a player standing on co-op land, read now so that leaving or being removed takes effect at once; null elsewhere.
    private async Task<CoopMembershipInfo?> MemberOnCoopLandAsync(PlayerPresence player, MapDefinition map, CancellationToken cancellationToken)
    {
        if (!map.IsCoop || _coops is null)
        {
            return null;
        }

        CoopMembershipInfo? member = await _coops.FindAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
        return member is not null && member.CoopId == player.Instance.OwnerId ? member : null;
    }

    // Co-op land keeps an audit line for every change of what the members hold (docs/adr/ADR-020).
    private void Audit(IUnitOfWork unit, CoopMembershipInfo? member, int itemId, int delta, string reason)
    {
        if (member is null)
        {
            return;
        }

        unit.Repository<CoopLogEntry>().Add(new CoopLogEntry { CoopId = member.CoopId, MemberId = member.MemberId, ItemId = itemId, Delta = delta, Reason = reason, OccurredAt = _time.GetUtcNow() });
    }

    // Rebuilds the walk grid and tells the farm layer and the growth rule what stands where.
    private void Rebuild(InstanceState state, MapInstance instance, MapDefinition map)
    {
        state.Rebuild(map);
        _tiles?.Publish(instance, state.Set.Items);
    }

    // Objects are changed only by the owner standing in their own place on a map that allows it; a visitor in somebody's place is refused (docs/adr/ADR-019 D3).
    private PlaceStatus TryEditablePlace(IConnection connection, PlaceAction action, out PlayerPresence player, out MapDefinition map)
    {
        player = null!;
        map = null!;
        if (!_presence.TryGet(connection, out PlayerPresence? found)
            || !_maps.TryGet(found.MapId, out MapDefinition? foundMap)
            || !foundMap.Placeable
            || !found.Instance.IsPrivate)
        {
            return PlaceStatus.NotPlaceable;
        }

        if (!VisitRules.MayDo(found.Instance, found.AccountId, action))
        {
            return PlaceStatus.NotYourPlace;
        }

        player = found;
        map = foundMap;
        return PlaceStatus.Ok;
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
        _tiles?.Publish(instance, state.Set.Items);
        return state;
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

    private sealed class GroundLayers : IGroundLayers
    {
        private readonly IReadOnlySet<(int X, int Y)> _tilled;
        private readonly IReadOnlySet<(int X, int Y)> _nodes;

        public GroundLayers(IReadOnlySet<(int X, int Y)> tilled, IReadOnlySet<(int X, int Y)> nodes)
        {
            _tilled = tilled;
            _nodes = nodes;
        }

        public bool IsTilled(int x, int y) => _tilled.Contains((x, y));

        public bool HasNode(int x, int y) => _nodes.Contains((x, y));
    }

    private sealed class InstanceState
    {
        public InstanceState(MapDefinition map) => this.Grid = map.Grid;

        public PlacedSet Set { get; } = new();

        // Replaced as a whole under the instance lock; movement validation reads it without one.
        public WalkGrid Grid;

        // The objects of each chunk, for "everything in chunk (cx, cy)" without a scan of the room. A piece is in every chunk its footprint overlaps.
        // Rebuilt with the grid, under the instance lock.
        public Dictionary<ChunkCoord, List<PlacedItem>> ByChunk { get; } = [];

        public void Rebuild(MapDefinition map)
        {
            Volatile.Write(ref this.Grid, this.Set.BuildGrid(map.Grid));
            this.ByChunk.Clear();
            foreach (PlacedItem item in this.Set.Items)
            {
                foreach (ChunkCoord chunk in PlacedChunks.RangeOf(item).Chunks())
                {
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
}
