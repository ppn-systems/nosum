// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Farm;

/// <summary>
/// <see cref="IFarmService"/>: validates a player's action with <see cref="FarmRules"/>, changes the
/// tile and the player's items in one transaction (through <see cref="IInventoryService"/>), then
/// tells everyone in the same instance and the quest service. The farm is private per player (docs/adr/ADR-015): the tiles belong to an instance (map plus owner), never to the map alone.
/// The owner is a player's account id on a private farm and a co-op's id on co-op land (docs/adr/ADR-020); the rules are the same code. On co-op land the player must belong to that co-op when the action
/// is committed, and every action writes an audit line in the same transaction. The tilled tiles of an instance are kept in memory after
/// the first read and written through to PostgreSQL; the server is a single process (docs/adr/ADR-009).
/// Crops are never simulated: growth counts watered game days and is computed from the stored day numbers (docs/adr/ADR-003 D4, ADR-021 D7);
/// bare soil that stayed dry may have reverted (<see cref="FarmRules.IsReverted"/>), which is decided on read. The day-ended seam (<see cref="IDayClock"/>)
/// only tidies up: it drops reverted tiles from memory and PostgreSQL and tells the players on the map.
/// </summary>
public sealed class FarmService : IFarmService
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IQuestService _quests;
    private readonly ICoopMembership _coops;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly GameCalendar _calendar;
    private readonly ConcurrentDictionary<string, Task<MapFarm>> _farms = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _mapLocks = new(16);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; the action uses the server's own record of the player's position.</param>
    /// <param name="maps">The hosted maps and their tillable tiles.</param>
    /// <param name="inventory">Seeds and harvests go through it.</param>
    /// <param name="quests">Told when a crop was planted or harvested.</param>
    /// <param name="coops">Who belongs to which co-op; asked for every action on co-op land.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The server clock crops grow by; tests pass a fake one.</param>
    /// <param name="calendar">The server calendar: seeds are planted by season, crops wither when theirs ends (docs/adr/ADR-018) and grow by watered game days (docs/adr/ADR-021 D7).</param>
    /// <param name="dayClock">Told when a game day ends, to sweep tiles whose bare soil reverted; <see langword="null"/> leaves the lazy rule alone.</param>
    public FarmService(
        IMapPresence presence,
        IMapCatalog maps,
        IInventoryService inventory,
        IQuestService quests,
        ICoopMembership coops,
        IUnitOfWorkFactory unitOfWork,
        TimeProvider time,
        GameCalendar calendar,
        IDayClock? dayClock = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(coops);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(calendar);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _quests = quests;
        _coops = coops;
        _unitOfWork = unitOfWork;
        _time = time;
        _calendar = calendar;
        if (dayClock is not null)
        {
            dayClock.DayEnded += this.OnDayEnded;
        }
    }

    /// <inheritdoc/>
    public async Task<FarmUseOutcome> UseAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if ((uint)hotbarSlot >= (uint)InventoryLimits.HotbarSlots)
        {
            return Refused(FarmStatus.InvalidSlot);
        }

        if (!_presence.TryGet(connection, out PlayerPresence? player)
            || !_maps.TryGet(player.MapId, out MapDefinition? map)
            || map.Tillable is not { } tillable)
        {
            return Refused(FarmStatus.NotOnFarm);
        }

        if (!tillable.Contains(tileX, tileY))
        {
            return Refused(FarmStatus.InvalidTile);
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        if (!FarmRules.InReach(feetX, feetY, tileX, tileY, map.Grid.TileSize))
        {
            return Refused(FarmStatus.OutOfReach);
        }

        Guid playerId = player.AccountId;
        MapFarm farm = await this.FarmOfAsync(player.Instance, cancellationToken).ConfigureAwait(false);
        FarmTile changedTile;
        FarmAction action;
        QuestEvent? report;

        using (await _mapLocks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            // Co-op land: the player must be a member of the co-op that owns this instance right now (a leave or a kick takes effect at once).
            CoopMembershipInfo? member = null;
            if (map.IsCoop)
            {
                member = await _coops.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (member is null || member.CoopId != player.Instance.OwnerId)
                {
                    return Refused(FarmStatus.NotOnFarm);
                }
            }

            long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            FarmTile? current = this.LiveTile(farm, player.Instance, tileX, tileY, nowMs);
            InventorySlotView held = await _inventory.PeekSlotAsync(playerId, hotbarSlot, cancellationToken).ConfigureAwait(false);

            Plan plan = PlanAction(held, current, tillable.IsTillable(tileX, tileY), nowMs);
            if (plan.Verdict != FarmVerdict.Ok)
            {
                return Refused(ToStatus(plan.Verdict));
            }

            FarmStatus status = await this.CommitAsync(playerId, member, player.Instance, tileX, tileY, plan, cancellationToken).ConfigureAwait(false);
            if (status != FarmStatus.Ok)
            {
                return Refused(status);
            }

            farm.Tiles[(tileX, tileY)] = plan.Next;
            changedTile = plan.Next;
            action = plan.Action;
            report = plan.Report;
        }

        await this.PushTileAsync(player.Instance, tileX, tileY, changedTile, cancellationToken).ConfigureAwait(false);
        await this.ReportAsync(playerId, report, cancellationToken).ConfigureAwait(false);
        return new FarmUseOutcome(FarmStatus.Ok, action);
    }

    /// <inheritdoc/>
    public async Task<FarmSnapshotView> GetFarmAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || map.Tillable is null)
        {
            return new FarmSnapshotView(string.Empty, nowMs, []);
        }

        MapFarm farm = await this.FarmOfAsync(player.Instance, cancellationToken).ConfigureAwait(false);
        using (await _mapLocks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            List<FarmTileView> tiles = new(farm.Tiles.Count);
            foreach (KeyValuePair<(int X, int Y), FarmTile> tile in farm.Tiles)
            {
                if (this.LiveTile(farm, player.Instance, tile.Key.X, tile.Key.Y, nowMs) is not null)
                {
                    tiles.Add(new FarmTileView(tile.Key.X, tile.Key.Y, tile.Value));
                }
            }

            return new FarmSnapshotView(map.Id, nowMs, tiles);
        }
    }

    /// <inheritdoc/>
    public void Forget(MapInstance instance) => _ = _farms.TryRemove(instance.Key, out _);

    private static FarmTileRow ToRow(int x, int y, FarmTile tile)
    {
        FarmTileRow row = FarmTileRow.Create();
        row.X = (ushort)x;
        row.Y = (ushort)y;
        row.CropId = tile.CropId;
        row.PlantedAtMs = tile.PlantedAtMs;
        row.GrowthDays = tile.GrowthDays;
        row.WateredDay = tile.WateredDay;
        return row;
    }

    private static FarmTileRow ToRemovedRow(int x, int y)
    {
        FarmTileRow row = FarmTileRow.Create();
        row.X = (ushort)x;
        row.Y = (ushort)y;
        row.Removed = true;
        return row;
    }

    private static FarmUseOutcome Refused(FarmStatus status) => new(status, FarmAction.None);

    // Decides what the held item does on the tile and what changes; nothing is written here.
    private Plan PlanAction(InventorySlotView held, FarmTile? current, bool tillable, long nowMs)
    {
        bool hasItem = held.ItemId != 0 && ItemCatalog.TryGet(held.ItemId, out _);
        _ = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item);

        // A mature crop is harvested whatever is held (bare hands included).
        bool withered = current is { HasCrop: true } standing && FarmRules.IsWithered(standing, nowMs, _calendar);
        if (current is { HasCrop: true } crop && !withered && FarmRules.IsMature(crop, nowMs, _calendar))
        {
            FarmVerdict harvest = FarmRules.Harvest(current, nowMs, _calendar, out FarmTile bare, out CropDefinition? harvested);
            return harvest != FarmVerdict.Ok
                ? Plan.Refuse(harvest)
                : new Plan(FarmVerdict.Ok, FarmAction.Harvested, bare, [InventoryOperation.Add(harvested!.HarvestItemId, harvested.HarvestQuantity)], "farm.harvest", new QuestEvent(QuestEventKind.CropHarvested, harvested.Id), harvested.HarvestItemId, harvested.HarvestQuantity);
        }

        if (!hasItem || item is null)
        {
            return Plan.Refuse(NoActionVerdict(current, withered));
        }

        switch (item.Kind)
        {
            case ItemKind.Tool when item.Tool == ToolKind.Hoe:
                {
                    FarmVerdict till = FarmRules.Till(tillable, current, nowMs, _calendar, out FarmTile tilled);
                    return till == FarmVerdict.Ok ? new Plan(till, FarmAction.Tilled, tilled, [], "farm.till", null, item.Id, 0) : Plan.Refuse(till);
                }

            case ItemKind.Tool when item.Tool == ToolKind.WateringCan:
                {
                    FarmVerdict water = FarmRules.Water(current, nowMs, _calendar, out FarmTile watered);
                    return water == FarmVerdict.Ok ? new Plan(water, FarmAction.Watered, watered, [], "farm.water", null, item.Id, 0) : Plan.Refuse(water);
                }

            case ItemKind.Seed:
                {
                    FarmVerdict plant = FarmRules.Plant(current, item.CropId, nowMs, _calendar, out FarmTile planted);
                    return plant == FarmVerdict.Ok
                        ? new Plan(plant, FarmAction.Planted, planted, [InventoryOperation.RemoveFromSlot(held.Slot, held.ItemId, 1)], "farm.plant", new QuestEvent(QuestEventKind.CropPlanted, item.CropId), item.Id, -1)
                        : Plan.Refuse(plant);
                }

            default:
                return Plan.Refuse(NoActionVerdict(current, withered));
        }
    }

    // Why a held item (or bare hands) does nothing on a tile that holds a crop.
    private static FarmVerdict NoActionVerdict(FarmTile? current, bool withered)
        => withered ? FarmVerdict.Withered : current is { HasCrop: true } ? FarmVerdict.NotMature : FarmVerdict.NothingToDo;

    private static FarmStatus ToStatus(FarmVerdict verdict) => verdict switch
    {
        FarmVerdict.Ok => FarmStatus.Ok,
        FarmVerdict.InvalidTile => FarmStatus.InvalidTile,
        FarmVerdict.OutOfReach => FarmStatus.OutOfReach,
        FarmVerdict.NotTillable => FarmStatus.NotTillable,
        FarmVerdict.AlreadyTilled => FarmStatus.AlreadyTilled,
        FarmVerdict.NotTilled => FarmStatus.NotTilled,
        FarmVerdict.HasCrop => FarmStatus.HasCrop,
        FarmVerdict.NotMature => FarmStatus.NotMature,
        FarmVerdict.OutOfSeason => FarmStatus.OutOfSeason,
        FarmVerdict.Withered => FarmStatus.Withered,
        _ => FarmStatus.NothingToDo,
    };

    private static FarmStatus ToStatus(InventoryStatus status) => status switch
    {
        InventoryStatus.Ok => FarmStatus.Ok,
        InventoryStatus.NoSpace => FarmStatus.InventoryFull,
        InventoryStatus.Conflict => FarmStatus.Conflict,
        _ => FarmStatus.ItemMissing,
    };

    // Writes the tile and the items in one transaction.
    private async Task<FarmStatus> CommitAsync(Guid playerId, CoopMembershipInfo? member, MapInstance instance, int x, int y, Plan plan, CancellationToken cancellationToken)
    {
        if (plan.Operations.Count > 0)
        {
            InventoryChange change = await _inventory.ApplyAsync(
                playerId,
                plan.Operations,
                plan.Reason,
                async (unit, ct) =>
                {
                    await this.UpsertAsync(unit, playerId, instance, x, y, plan.Next, ct).ConfigureAwait(false);
                    this.Audit(unit, member, plan);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            return ToStatus(change.Status);
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            await this.UpsertAsync(unit, playerId, instance, x, y, plan.Next, cancellationToken).ConfigureAwait(false);
            this.Audit(unit, member, plan);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return FarmStatus.Ok;
    }

    // Co-op land only: one audit line per action, in the same transaction as the tile and the items (docs/adr/ADR-020 D5).
    private void Audit(IUnitOfWork unit, CoopMembershipInfo? member, Plan plan)
    {
        if (member is null)
        {
            return;
        }

        unit.Repository<CoopLogEntry>().Add(new CoopLogEntry
        {
            CoopId = member.CoopId,
            MemberId = member.MemberId,
            ItemId = plan.AuditItemId,
            Delta = plan.AuditDelta,
            Reason = CoopLand.AuditReason(plan.Reason),
            OccurredAt = _time.GetUtcNow(),
        });
    }

    private async Task UpsertAsync(IUnitOfWork unit, Guid playerId, MapInstance instance, int x, int y, FarmTile tile, CancellationToken cancellationToken)
    {
        IRepository<FarmPlot> plots = unit.Repository<FarmPlot>();
        FarmPlot? row = await plots.FirstOrDefaultAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId && p.X == x && p.Y == y, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new FarmPlot { MapId = instance.MapId, OwnerId = instance.OwnerId, X = x, Y = y };
            plots.Add(row);
        }

        row.CropId = tile.CropId;
        row.PlantedAtMs = tile.PlantedAtMs;
        row.GrowthDays = tile.GrowthDays;
        row.WateredDay = tile.WateredDay;
        row.TendedDay = tile.TendedDay;
        row.LastWorkedBy = playerId;
        row.UpdatedAt = _time.GetUtcNow();
    }

    // Loads a map's tilled tiles once; a failed load is forgotten so the next call tries again.
    private async Task<MapFarm> FarmOfAsync(MapInstance instance, CancellationToken cancellationToken)
    {
        string key = instance.Key;
        Task<MapFarm> load = _farms.GetOrAdd(key, _ => this.LoadFarmAsync(instance));
        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (load.IsFaulted)
        {
            _ = _farms.TryRemove(new KeyValuePair<string, Task<MapFarm>>(key, load));
            throw;
        }
    }

    private async Task<MapFarm> LoadFarmAsync(MapInstance instance)
    {
        MapFarm farm = new(instance);
        int today = _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<FarmPlot> rows = await unit.Repository<FarmPlot>().ListAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
            foreach (FarmPlot row in rows)
            {
                FarmTile tile = new(row.CropId, row.PlantedAtMs, row.GrowthDays, row.WateredDay, row.TendedDay);

                // Bare soil from before day counting has no history: it starts its dry days now instead of reverting on the spot.
                if (!tile.HasCrop && tile.WateredDay == 0 && tile.TendedDay == 0)
                {
                    tile = tile with { TendedDay = today };
                }

                farm.Tiles[(row.X, row.Y)] = tile;
            }
        }

        return farm;
    }

    private async Task PushTileAsync(MapInstance instance, int x, int y, FarmTile tile, CancellationToken cancellationToken)
    {
        using PacketScope<FarmTilesChanged> lease = PacketFactory<FarmTilesChanged>.Acquire();
        FarmTilesChanged packet = lease.Value;
        packet.MapId = instance.MapId;
        packet.ServerNowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        packet.Tiles.Clear();
        packet.Tiles.Add(ToRow(x, y, tile));
        await _presence.MulticastAsync(instance.Key, packet, null, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031", Justification = "The action is committed and pushed; a failing quest update must not turn it into an error reply.")]
    private async Task ReportAsync(Guid playerId, QuestEvent? report, CancellationToken cancellationToken)
    {
        if (report is not { } questEvent)
        {
            return;
        }

        try
        {
            await _quests.ReportAsync(playerId, questEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Quest progress is best effort here; the next matching action counts again.
        }
    }

    // A tile as the player sees it: null when it was never tilled or its bare soil has reverted. A reverted tile may still sit in memory and in
    // PostgreSQL until the next day sweep; whoever tills it again overwrites it.
    private FarmTile? LiveTile(MapFarm farm, MapInstance instance, int x, int y, long nowMs)
        => farm.Tiles.TryGetValue((x, y), out FarmTile found) && !FarmRules.IsReverted(found, instance.Key, x, y, nowMs, _calendar) ? found : null;

    private void OnDayEnded(GameDayEnded ended)
    {
        _ = ended;
        _ = this.SweepAsync();
    }

    // Drops the reverted tiles of every loaded farm. Not loaded farms need nothing: the rule is computed on read.
    [SuppressMessage("Design", "CA1031", Justification = "A failed sweep is repeated at the next day end; the reverted tiles are already invisible through the lazy rule.")]
    private async Task SweepAsync()
    {
        try
        {
            foreach (Task<MapFarm> load in _farms.Values.ToArray())
            {
                if (load.IsCompletedSuccessfully)
                {
                    await this.SweepFarmAsync(await load.ConfigureAwait(false)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            // See the suppression.
        }
    }

    private async Task SweepFarmAsync(MapFarm farm)
    {
        MapInstance instance = farm.Instance;
        List<(int X, int Y)> gone = [];
        using (await _mapLocks.EnterAsync(instance.Key, CancellationToken.None).ConfigureAwait(false))
        {
            long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            foreach (KeyValuePair<(int X, int Y), FarmTile> tile in farm.Tiles)
            {
                if (FarmRules.IsReverted(tile.Value, instance.Key, tile.Key.X, tile.Key.Y, nowMs, _calendar))
                {
                    gone.Add(tile.Key);
                }
            }

            if (gone.Count == 0)
            {
                return;
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<FarmPlot> plots = unit.Repository<FarmPlot>();
                foreach (FarmPlot row in await plots.ListAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false))
                {
                    if (gone.Contains((row.X, row.Y)))
                    {
                        plots.Remove(row);
                    }
                }

                _ = await unit.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            foreach ((int X, int Y) key in gone)
            {
                _ = farm.Tiles.Remove(key);
            }
        }

        const int BatchSize = 64;
        for (int start = 0; start < gone.Count; start += BatchSize)
        {
            using PacketScope<FarmTilesChanged> lease = PacketFactory<FarmTilesChanged>.Acquire();
            FarmTilesChanged packet = lease.Value;
            packet.MapId = instance.MapId;
            packet.ServerNowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            packet.Tiles.Clear();
            for (int i = start; i < Math.Min(gone.Count, start + BatchSize); i++)
            {
                packet.Tiles.Add(ToRemovedRow(gone[i].X, gone[i].Y));
            }

            await _presence.MulticastAsync(instance.Key, packet, null, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class MapFarm(MapInstance instance)
    {
        public MapInstance Instance { get; } = instance;

        public Dictionary<(int X, int Y), FarmTile> Tiles { get; } = [];
    }

    private readonly record struct Plan(FarmVerdict Verdict, FarmAction Action, FarmTile Next, IReadOnlyList<InventoryOperation> Operations, string Reason, QuestEvent? Report, int AuditItemId = 0, int AuditDelta = 0)
    {
        public static Plan Refuse(FarmVerdict verdict) => new(verdict, FarmAction.None, default, [], string.Empty, null);
    }
}
