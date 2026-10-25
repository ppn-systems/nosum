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
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// <see cref="IFurnaceService"/>: a job is a <see cref="FurnaceJob"/> row (the ore and the start time); the ore and the coal leave the inventory in the same transaction
/// that inserts it, and collecting deletes it in the same transaction that adds the bar. Whether the bar is ready is <see cref="FurnaceRules.StateOf"/> of the stored start
/// and the server clock, so nothing ticks and a restart loses nothing (docs/adr/ADR-024 D4). Every change takes the instance's lock, so it cannot interleave with the
/// furnace being moved or picked up; picking one up with a job inside is refused.
/// </summary>
public sealed class FurnaceService : IFurnaceService, IPlacedBehaviour
{
    private const string SmeltReason = "furnace.smelt";
    private const string CollectReason = "furnace.collect";

    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IPlaceableService _placeables;
    private readonly IPlacedLookup _placed;
    private readonly IInstanceLock _locks;
    private readonly IInventoryService _inventory;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ICoopMembership? _coops;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every request uses the server's own record of the player.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="placeables">Finds the placed furnace.</param>
    /// <param name="placed">Lists the furnaces of an instance.</param>
    /// <param name="locks">The per-instance lock shared with the placeables.</param>
    /// <param name="inventory">Moves the items.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="coops">Co-op membership, asked on co-op land. <see langword="null"/> when there is no co-op land.</param>
    /// <param name="calendar">The server calendar: how long 30 game minutes are.</param>
    /// <param name="time">The server clock jobs are stamped and read with; tests pass a fake one.</param>
    public FurnaceService(
        IMapPresence presence,
        IMapCatalog maps,
        IPlaceableService placeables,
        IPlacedLookup placed,
        IInstanceLock locks,
        IInventoryService inventory,
        IUnitOfWorkFactory unitOfWork,
        ICoopMembership? coops,
        GameCalendar calendar,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _maps = maps;
        _placeables = placeables;
        _placed = placed;
        _locks = locks;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _coops = coops;
        _calendar = calendar;
        _time = time;
    }

    /// <inheritdoc/>
    PlaceableKind IPlacedBehaviour.Kind => PlaceableKind.Furnace;

    /// <inheritdoc/>
    public async Task<FurnaceOutcome> StartAsync(IConnection connection, int objectId, int oreItemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (FurnaceStatus status, PlayerPresence? player, CoopMembershipInfo? member) = await this.AuthorizeAsync(connection, objectId, cancellationToken).ConfigureAwait(false);
        if (status != FurnaceStatus.Ok || player is null)
        {
            return Refused(status, objectId);
        }

        if (!FurnaceRules.TryBarOf(oreItemId, out _))
        {
            return Refused(FurnaceStatus.NotSmeltable, objectId);
        }

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        FurnaceStatus failure = FurnaceStatus.Ok;
        using (await _locks.EnterAsync(player.Instance, cancellationToken).ConfigureAwait(false))
        {
            InventoryChange change = await _inventory.ApplyAsync(
                player.AccountId,
                [InventoryOperation.Remove(oreItemId, FurnaceRules.OreCost), InventoryOperation.Remove(ItemCatalog.Coal, FurnaceRules.CoalCost)],
                SmeltReason,
                async (unit, ct) =>
                {
                    if (await unit.Repository<PlacedObject>().FindAsync(objectId, ct).ConfigureAwait(false) is null)
                    {
                        failure = FurnaceStatus.UnknownObject;
                        return false;
                    }

                    if (await unit.Repository<FurnaceJob>().FindAsync(objectId, ct).ConfigureAwait(false) is not null)
                    {
                        failure = FurnaceStatus.Busy;
                        return false;
                    }

                    unit.Repository<FurnaceJob>().Add(new FurnaceJob { ObjectId = objectId, OreItemId = oreItemId, StartedAtMs = nowMs });
                    this.Audit(unit, member, oreItemId, -FurnaceRules.OreCost, CoopLand.SmeltReason);
                    this.Audit(unit, member, ItemCatalog.Coal, -FurnaceRules.CoalCost, CoopLand.SmeltReason);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return Refused(failure != FurnaceStatus.Ok ? failure : change.Status == InventoryStatus.NotEnough ? FurnaceStatus.NotEnough : FurnaceStatus.Conflict, objectId);
            }
        }

        await this.PushAsync(player.Instance, new FurnaceView(objectId, FurnaceStateCode.Working, oreItemId, (int)(FurnaceRules.ReadyAtMs(nowMs, _calendar) - nowMs)), cancellationToken).ConfigureAwait(false);
        return new FurnaceOutcome(FurnaceStatus.Ok, objectId, 0, 0);
    }

    /// <inheritdoc/>
    public async Task<FurnaceOutcome> CollectAsync(IConnection connection, int objectId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        (FurnaceStatus status, PlayerPresence? player, CoopMembershipInfo? member) = await this.AuthorizeAsync(connection, objectId, cancellationToken).ConfigureAwait(false);
        if (status != FurnaceStatus.Ok || player is null)
        {
            return Refused(status, objectId);
        }

        int barItemId = 0;
        FurnaceStatus failure = FurnaceStatus.Ok;
        using (await _locks.EnterAsync(player.Instance, cancellationToken).ConfigureAwait(false))
        {
            FurnaceJob? job = await this.ReadJobAsync(objectId, cancellationToken).ConfigureAwait(false);
            if (job is null || !FurnaceRules.TryBarOf(job.OreItemId, out barItemId))
            {
                return Refused(FurnaceStatus.NothingToCollect, objectId);
            }

            if (FurnaceRules.StateOf(job.StartedAtMs, _time.GetUtcNow().ToUnixTimeMilliseconds(), _calendar) != FurnaceState.Ready)
            {
                return Refused(FurnaceStatus.NotReady, objectId);
            }

            InventoryChange change = await _inventory.ApplyAsync(
                player.AccountId,
                [InventoryOperation.Add(barItemId, 1)],
                CollectReason,
                async (unit, ct) =>
                {
                    FurnaceJob? row = await unit.Repository<FurnaceJob>().FindAsync(objectId, ct).ConfigureAwait(false);
                    if (row is null)
                    {
                        failure = FurnaceStatus.NothingToCollect;
                        return false;
                    }

                    unit.Repository<FurnaceJob>().Remove(row);
                    this.Audit(unit, member, barItemId, 1, CoopLand.CollectReason);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return Refused(failure != FurnaceStatus.Ok ? failure : change.Status == InventoryStatus.NoSpace ? FurnaceStatus.NoSpace : FurnaceStatus.Conflict, objectId);
            }
        }

        await this.PushAsync(player.Instance, new FurnaceView(objectId, FurnaceStateCode.Idle, 0, 0), cancellationToken).ConfigureAwait(false);
        return new FurnaceOutcome(FurnaceStatus.Ok, objectId, barItemId, 1);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FurnaceView>> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || !map.Placeable)
        {
            return [];
        }

        List<int> ids = [];
        foreach (PlacedItem item in _placed.ItemsOf(player.Instance))
        {
            if (PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition) && definition.Kind == PlaceableKind.Furnace)
            {
                ids.Add(item.Id);
            }
        }

        if (ids.Count == 0)
        {
            return [];
        }

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        List<FurnaceView> views = [];
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            foreach (FurnaceJob job in await unit.Repository<FurnaceJob>().ListAsync(j => ids.Contains(j.ObjectId), cancellationToken).ConfigureAwait(false))
            {
                views.Add(ViewOf(job, nowMs));
            }
        }

        return views;
    }

    /// <inheritdoc/>
    IReadOnlyList<Guid> IPlacedBehaviour.OwnersOf(PlacedItem item) => [];

    /// <inheritdoc/>
    async Task<PlaceStatus> IPlacedBehaviour.JoinPickUpAsync(PlacedItem item, IUnitOfWork unit, CancellationToken cancellationToken)
        => await unit.Repository<FurnaceJob>().AnyAsync(j => j.ObjectId == item.Id, cancellationToken).ConfigureAwait(false) ? PlaceStatus.InUse : PlaceStatus.Ok;

    /// <inheritdoc/>
    ValueTask IPlacedBehaviour.PickedUpAsync(MapInstance instance, PlacedItem item, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private static FurnaceOutcome Refused(FurnaceStatus status, int objectId) => new(status, objectId, 0, 0);

    private FurnaceView ViewOf(FurnaceJob job, long nowMs)
    {
        long readyAt = FurnaceRules.ReadyAtMs(job.StartedAtMs, _calendar);
        return nowMs >= readyAt
            ? new FurnaceView(job.ObjectId, FurnaceStateCode.Ready, job.OreItemId, 0)
            : new FurnaceView(job.ObjectId, FurnaceStateCode.Working, job.OreItemId, (int)Math.Min(int.MaxValue, readyAt - nowMs));
    }

    private async Task<FurnaceJob?> ReadJobAsync(int objectId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<FurnaceJob>().FindAsync(objectId, cancellationToken).ConfigureAwait(false);
        }
    }

    // The same checks for every request: the player is somewhere with furnaces, the furnace stands in their instance, they may use it and are within reach.
    private async Task<(FurnaceStatus Status, PlayerPresence? Player, CoopMembershipInfo? Member)> AuthorizeAsync(IConnection connection, int objectId, CancellationToken cancellationToken)
    {
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || !map.Placeable)
        {
            return (FurnaceStatus.UnknownObject, null, null);
        }

        PlacedItem? found = await _placeables.FindAsync(player.Instance, objectId, cancellationToken).ConfigureAwait(false);
        if (found is not { } furnace || !PlaceableCatalog.TryGet(furnace.ItemId, out PlaceableDefinition? definition) || definition.Kind != PlaceableKind.Furnace)
        {
            return (FurnaceStatus.UnknownObject, null, null);
        }

        CoopMembershipInfo? member = null;
        if (map.IsCoop && _coops is not null)
        {
            CoopMembershipInfo? info = await _coops.FindAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
            member = info is not null && info.CoopId == player.Instance.OwnerId ? info : null;
        }

        if (!PlacedAccess.MayUse(player.Instance, player.AccountId, member is not null))
        {
            return (FurnaceStatus.NotYours, null, null);
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        PlaceableView view = definition.View(Math.Min(furnace.Rotation, definition.Rotations - 1));
        return PlacedAccess.Reaches(view, furnace.TileX, furnace.TileY, feetX, feetY, map.Grid.TileSize) ? (FurnaceStatus.Ok, player, member) : (FurnaceStatus.OutOfReach, null, null);
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

    private async Task PushAsync(MapInstance instance, FurnaceView view, CancellationToken cancellationToken)
    {
        using PacketScope<FurnaceChanged> lease = PacketFactory<FurnaceChanged>.Acquire();
        FurnaceChanged packet = lease.Value;
        packet.ObjectId = view.ObjectId;
        packet.State = view.State;
        packet.OreItemId = view.OreItemId;
        packet.RemainingMs = view.RemainingMs;
        await _presence.MulticastAsync(instance.Key, packet, null, cancellationToken).ConfigureAwait(false);
    }
}
