// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Farm;

/// <summary>
/// <see cref="IFarmService"/>: validates a player's action with <see cref="FarmRules"/>, changes the
/// tile and the player's items in one transaction (through <see cref="IInventoryService"/>), then
/// tells everyone in the same instance and the quest service. The farm is private per player (docs/adr/ADR-015): the tiles belong to an instance (map plus owner), never to the map alone. The tilled tiles of an instance are kept in memory after
/// the first read and written through to PostgreSQL; the server is a single process (docs/adr/ADR-009).
/// Crops are never simulated: growth is computed from the stored timestamps (docs/adr/ADR-003 D4).
/// </summary>
public sealed class FarmService : IFarmService
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IQuestService _quests;
    private readonly IGroundItems _groundItems;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Task<MapFarm>> _farms = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _mapLocks = new(16);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; the action uses the server's own record of the player's position.</param>
    /// <param name="maps">The hosted maps and their tillable tiles.</param>
    /// <param name="inventory">Seeds and harvests go through it.</param>
    /// <param name="quests">Told when a crop was planted or harvested.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The server clock crops grow by; tests pass a fake one.</param>
    /// <param name="groundItems">A harvest pops its produce onto the ground through it (docs/adr/ADR-021 D5).</param>
    public FarmService(
        IMapPresence presence,
        IMapCatalog maps,
        IInventoryService inventory,
        IQuestService quests,
        IUnitOfWorkFactory unitOfWork,
        TimeProvider time,
        IGroundItems groundItems)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(groundItems);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _quests = quests;
        _unitOfWork = unitOfWork;
        _time = time;
        _groundItems = groundItems;
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
        PendingGroundDrop? pending;

        using (await _mapLocks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            FarmTile? current = farm.Tiles.TryGetValue((tileX, tileY), out FarmTile found) ? found : null;
            InventorySlotView held = await _inventory.PeekSlotAsync(playerId, hotbarSlot, cancellationToken).ConfigureAwait(false);

            Plan plan = PlanAction(held, current, tillable.IsTillable(tileX, tileY), nowMs);
            if (plan.Verdict != FarmVerdict.Ok)
            {
                return Refused(ToStatus(plan.Verdict));
            }

            GroundDrop? drop = null;
            if (plan.Drop is { } produce)
            {
                // The produce pops beside the plot, nudged toward the harvester so it can be picked up at once (docs/adr/ADR-021 D5).
                int tileSize = map.Grid.TileSize;
                GroundItemRules.PopPosition((tileX + 0.5) * tileSize, (tileY + 0.5) * tileSize, feetX, feetY, out double dropX, out double dropY);
                drop = new GroundDrop(player.Instance, (int)dropX, (int)dropY, produce.ItemId, produce.Quantity, playerId);
            }

            (FarmStatus status, pending) = await this.CommitAsync(playerId, player.Instance, tileX, tileY, plan, drop, cancellationToken).ConfigureAwait(false);
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
        if (pending is not null)
        {
            await _groundItems.CompleteAsync(pending, cancellationToken).ConfigureAwait(false);
        }

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
                tiles.Add(new FarmTileView(tile.Key.X, tile.Key.Y, tile.Value));
            }

            return new FarmSnapshotView(map.Id, nowMs, tiles);
        }
    }

    private static FarmTileRow ToRow(int x, int y, FarmTile tile)
    {
        FarmTileRow row = FarmTileRow.Create();
        row.X = (ushort)x;
        row.Y = (ushort)y;
        row.CropId = tile.CropId;
        row.PlantedAtMs = tile.PlantedAtMs;
        row.GrowthMs = tile.GrowthMs;
        row.SettledAtMs = tile.SettledAtMs;
        row.WetUntilMs = tile.WetUntilMs;
        return row;
    }

    private static FarmUseOutcome Refused(FarmStatus status) => new(status, FarmAction.None);

    // Decides what the held item does on the tile and what changes; nothing is written here.
    private static Plan PlanAction(InventorySlotView held, FarmTile? current, bool tillable, long nowMs)
    {
        bool hasItem = held.ItemId != 0 && ItemCatalog.TryGet(held.ItemId, out _);
        _ = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item);

        // A mature crop is harvested whatever is held (bare hands included).
        if (current is { HasCrop: true } crop && FarmRules.IsMature(crop, nowMs))
        {
            FarmVerdict harvest = FarmRules.Harvest(current, nowMs, out FarmTile bare, out CropDefinition? harvested);
            return harvest != FarmVerdict.Ok
                ? Plan.Refuse(harvest)
                : new Plan(FarmVerdict.Ok, FarmAction.Harvested, bare, [], "farm.harvest", new QuestEvent(QuestEventKind.CropHarvested, harvested!.Id), new PlannedDrop(harvested.HarvestItemId, harvested.HarvestQuantity));
        }

        if (!hasItem || item is null)
        {
            return Plan.Refuse(current is { HasCrop: true } ? FarmVerdict.NotMature : FarmVerdict.NothingToDo);
        }

        switch (item.Kind)
        {
            case ItemKind.Tool when item.Tool == ToolKind.Hoe:
                {
                    FarmVerdict till = FarmRules.Till(tillable, current, out FarmTile tilled);
                    return till == FarmVerdict.Ok ? new Plan(till, FarmAction.Tilled, tilled, [], "farm.till", null) : Plan.Refuse(till);
                }

            case ItemKind.Tool when item.Tool == ToolKind.WateringCan:
                {
                    FarmVerdict water = FarmRules.Water(current, nowMs, out FarmTile watered);
                    return water == FarmVerdict.Ok ? new Plan(water, FarmAction.Watered, watered, [], "farm.water", null) : Plan.Refuse(water);
                }

            case ItemKind.Seed:
                {
                    FarmVerdict plant = FarmRules.Plant(current, item.CropId, nowMs, out FarmTile planted);
                    return plant == FarmVerdict.Ok
                        ? new Plan(plant, FarmAction.Planted, planted, [InventoryOperation.RemoveFromSlot(held.Slot, held.ItemId, 1)], "farm.plant", new QuestEvent(QuestEventKind.CropPlanted, item.CropId))
                        : Plan.Refuse(plant);
                }

            default:
                return Plan.Refuse(current is { HasCrop: true } ? FarmVerdict.NotMature : FarmVerdict.NothingToDo);
        }
    }

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
        _ => FarmStatus.NothingToDo,
    };

    private static FarmStatus ToStatus(InventoryStatus status) => status switch
    {
        InventoryStatus.Ok => FarmStatus.Ok,
        InventoryStatus.NoSpace => FarmStatus.InventoryFull,
        InventoryStatus.Conflict => FarmStatus.Conflict,
        _ => FarmStatus.ItemMissing,
    };

    // Writes the tile and the items in one transaction; a harvest's ground item rows join the tile's transaction, so a crash
    // cannot reset the plot without leaving the produce.
    private async Task<(FarmStatus Status, PendingGroundDrop? Pending)> CommitAsync(Guid playerId, MapInstance instance, int x, int y, Plan plan, GroundDrop? drop, CancellationToken cancellationToken)
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
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            return (ToStatus(change.Status), null);
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            await this.UpsertAsync(unit, playerId, instance, x, y, plan.Next, cancellationToken).ConfigureAwait(false);
            PendingGroundDrop? pending = drop is { } staged ? _groundItems.Stage(unit, staged) : null;
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (FarmStatus.Ok, pending);
        }
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
        row.GrowthMs = tile.GrowthMs;
        row.SettledAtMs = tile.SettledAtMs;
        row.WetUntilMs = tile.WetUntilMs;
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
        MapFarm farm = new();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<FarmPlot> rows = await unit.Repository<FarmPlot>().ListAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
            foreach (FarmPlot row in rows)
            {
                farm.Tiles[(row.X, row.Y)] = new FarmTile(row.CropId, row.PlantedAtMs, row.GrowthMs, row.SettledAtMs, row.WetUntilMs);
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

    private sealed class MapFarm
    {
        public Dictionary<(int X, int Y), FarmTile> Tiles { get; } = [];
    }

    private readonly record struct PlannedDrop(int ItemId, int Quantity);

    private readonly record struct Plan(FarmVerdict Verdict, FarmAction Action, FarmTile Next, IReadOnlyList<InventoryOperation> Operations, string Reason, QuestEvent? Report, PlannedDrop? Drop = null)
    {
        public static Plan Refuse(FarmVerdict verdict) => new(verdict, FarmAction.None, default, [], string.Empty, null);
    }
}
