// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Node.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Skills;
using Nosum.Gameplay.Visits;
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
public sealed class FarmService : IFarmService, IFarmTiles
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IQuestService _quests;
    private readonly ICoopMembership _coops;
    private readonly IResourceNodeService _nodes;
    private readonly IToolState _tools;
    private readonly IGroundItems _groundItems;
    private readonly IPlayerProgress _progress;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly GameCalendar _calendar;
    private readonly IPlacedLookup? _placed;
    private readonly ConcurrentDictionary<string, Task<MapFarm>> _farms = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _mapLocks = new(16);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; the action uses the server's own record of the player's position.</param>
    /// <param name="maps">The hosted maps and their tillable tiles.</param>
    /// <param name="inventory">Seeds and harvests go through it.</param>
    /// <param name="quests">Told when a crop was planted or harvested.</param>
    /// <param name="coops">Who belongs to which co-op; asked for every action on co-op land.</param>
    /// <param name="nodes">Trees, stones and weeds: it handles a tool aimed at one, water and tree seeds (docs/adr/ADR-021).</param>
    /// <param name="tools">The swing cooldown and the watering can's water.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The server clock crops grow by; tests pass a fake one.</param>
    /// <param name="calendar">The server calendar: seeds are planted by season, crops wither when theirs ends (docs/adr/ADR-018) and grow by watered game days (docs/adr/ADR-021 D7).</param>
    /// <param name="groundItems">A harvest pops its produce onto the ground through it (docs/adr/ADR-021 D5).</param>
    /// <param name="progress">Every successful tool use spends energy and earns experience through it, in the transaction of its effect (docs/adr/ADR-023).</param>
    /// <param name="dayClock">Told when a game day ends, to sweep tiles whose bare soil reverted; <see langword="null"/> leaves the lazy rule alone.</param>
    /// <param name="placed">What players placed on the ground: soil under a piece cannot be worked (docs/adr/ADR-024 D3). <see langword="null"/> when nothing can be placed.</param>
    public FarmService(
        IMapPresence presence,
        IMapCatalog maps,
        IInventoryService inventory,
        IQuestService quests,
        ICoopMembership coops,
        IResourceNodeService nodes,
        IToolState tools,
        IUnitOfWorkFactory unitOfWork,
        TimeProvider time,
        GameCalendar calendar,
        IGroundItems groundItems,
        IPlayerProgress progress,
        IDayClock? dayClock = null,
        IPlacedLookup? placed = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(coops);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(groundItems);
        ArgumentNullException.ThrowIfNull(progress);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _quests = quests;
        _coops = coops;
        _nodes = nodes;
        _tools = tools;
        _unitOfWork = unitOfWork;
        _time = time;
        _calendar = calendar;
        _groundItems = groundItems;
        _progress = progress;
        _placed = placed;
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
            || (map.Tillable is null && !map.HasNodes))
        {
            return Refused(FarmStatus.NotOnFarm);
        }

        if ((uint)tileX >= (uint)map.Grid.Width || (uint)tileY >= (uint)map.Grid.Height)
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
        InventorySlotView held = await _inventory.PeekSlotAsync(playerId, hotbarSlot, cancellationToken).ConfigureAwait(false);
        _ = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? heldItem);

        // One swing per cooldown, whatever the tool does (docs/adr/ADR-021 D8).
        if (heldItem is { Kind: ItemKind.Tool } && !_tools.TryBeginSwing(playerId))
        {
            return Refused(FarmStatus.Cooldown);
        }

        // Co-op land is for its members only, whoever swings what (the check is repeated under the lock for the farm layer).
        if (map.IsCoop && (await _coops.FindAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } coopMember || coopMember.CoopId != player.Instance.OwnerId))
        {
            return Refused(FarmStatus.NotOnFarm);
        }

        // A visitor may only water (docs/adr/ADR-019 D3): no chopping, breaking, tilling, planting or harvesting on somebody else's land.
        if (VisitRules.IsVisitor(player.Instance, playerId) && !IsWateringCan(held))
        {
            return Refused(FarmStatus.NotYourFarm);
        }

        // Resource nodes, water and tree seeds belong to the node layer; it answers null when the farm layer decides.
        MapFarm? farm = null;
        bool worked = false;
        if (map.Tillable is not null)
        {
            farm = await this.FarmOfAsync(player.Instance, cancellationToken).ConfigureAwait(false);
            if (heldItem is { Kind: ItemKind.TreeSeed })
            {
                using (await _mapLocks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
                {
                    worked = farm.Tiles.ContainsKey((tileX, tileY));
                }
            }
        }

        if (await _nodes.TryUseAsync(new NodeUseRequest(player, map, hotbarSlot, held, tileX, tileY, worked), cancellationToken).ConfigureAwait(false) is { } handled)
        {
            return handled;
        }

        if (map.Tillable is not { } tillable || farm is null)
        {
            return Refused(FarmStatus.NotOnFarm);
        }

        FarmTile changedTile;
        FarmAction action;
        QuestEvent? report;
        bool cleared;
        PendingGroundDrop? pending;

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
            if (current is null && _placed?.IsCovered(player.Instance, tileX, tileY) == true)
            {
                return Refused(FarmStatus.Blocked);
            }

            Plan plan = PlanAction(held, current, tillable.IsTillable(tileX, tileY), nowMs);
            if (plan.Verdict != FarmVerdict.Ok)
            {
                return Refused(ToStatus(plan.Verdict));
            }

            if (plan.Action == FarmAction.Watered && _tools.WaterLeft(playerId) <= 0)
            {
                return Refused(FarmStatus.CanEmpty);
            }

            if (!VisitRules.MayDo(player.Instance, playerId, RuleOf(plan.Action)))
            {
                return Refused(FarmStatus.NotYourFarm);
            }

            GroundDrop? drop = null;
            if (plan.Drop is { } produce)
            {
                // The produce pops beside the plot, nudged toward the harvester so it can be picked up at once (docs/adr/ADR-021 D5).
                int tileSize = map.Grid.TileSize;
                GroundItemRules.PopPosition((tileX + 0.5) * tileSize, (tileY + 0.5) * tileSize, feetX, feetY, out double dropX, out double dropY);
                drop = new GroundDrop(player.Instance, (int)dropX, (int)dropY, produce.ItemId, produce.Quantity, playerId);
            }

            (FarmStatus status, pending) = await this.CommitAsync(playerId, member, player.Instance, tileX, tileY, plan, drop, cancellationToken).ConfigureAwait(false);
            if (status != FarmStatus.Ok)
            {
                return Refused(status);
            }

            cleared = plan.Action == FarmAction.Cleared;
            if (cleared)
            {
                _ = farm.Tiles.Remove((tileX, tileY));
            }
            else
            {
                farm.Tiles[(tileX, tileY)] = plan.Next;
            }

            changedTile = plan.Next;
            action = plan.Action;
            report = plan.Report;
        }

        if (action == FarmAction.Watered)
        {
            _ = await _tools.UseWaterAsync(playerId, cancellationToken).ConfigureAwait(false);
        }

        if (cleared)
        {
            await this.PushClearedAsync(player.Instance, tileX, tileY, cancellationToken).ConfigureAwait(false);
            return new FarmUseOutcome(FarmStatus.Ok, action);
        }

        await this.PushTileAsync(player.Instance, tileX, tileY, changedTile, cancellationToken).ConfigureAwait(false);
        if (pending is not null)
        {
            await _groundItems.CompleteAsync(pending, cancellationToken).ConfigureAwait(false);

            // The produce lands within reach: a harvester standing still takes it now, not on their next step.
            await _groundItems.OnPlayerMovedAsync(player).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task<IReadOnlySet<(int X, int Y)>> WorkedTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default)
    {
        HashSet<(int X, int Y)> found = [];
        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || map.Tillable is null)
        {
            return found;
        }

        MapFarm farm = await this.FarmOfAsync(instance, cancellationToken).ConfigureAwait(false);
        using (await _mapLocks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            for (int y = tileY; y < tileY + height; y++)
            {
                for (int x = tileX; x < tileX + width; x++)
                {
                    if (farm.Tiles.ContainsKey((x, y)))
                    {
                        _ = found.Add((x, y));
                    }
                }
            }
        }

        return found;
    }

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

    private static bool IsWateringCan(InventorySlotView held)
        => held.ItemId != 0 && ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item) && item.Kind == ItemKind.Tool && item.Tool == ToolKind.WateringCan;

    private static PlaceAction RuleOf(FarmAction action) => action switch
    {
        FarmAction.Tilled => PlaceAction.Till,
        FarmAction.Planted => PlaceAction.Plant,
        FarmAction.Harvested => PlaceAction.Harvest,
        _ => PlaceAction.Water,
    };

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
                : new Plan(FarmVerdict.Ok, FarmAction.Harvested, bare, [], "farm.harvest", new QuestEvent(QuestEventKind.CropHarvested, harvested!.Id), harvested.HarvestItemId, harvested.HarvestQuantity, new PlannedDrop(harvested.HarvestItemId, harvested.HarvestQuantity));
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

            case ItemKind.Tool when item.Tool == ToolKind.Pickaxe:
                {
                    FarmVerdict clear = FarmRules.Clear(current);
                    return clear == FarmVerdict.Ok ? new Plan(clear, FarmAction.Cleared, default, [], "farm.clear", null, item.Id, 0) : Plan.Refuse(clear);
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

    // Writes the tile and the items in one transaction; a harvest's ground item rows join the tile's transaction, so a crash
    // cannot reset the plot without leaving the produce.
    private async Task<(FarmStatus Status, PendingGroundDrop? Pending)> CommitAsync(Guid playerId, CoopMembershipInfo? member, MapInstance instance, int x, int y, Plan plan, GroundDrop? drop, CancellationToken cancellationToken)
    {
        // Energy and experience are charged in the same transaction as the effect; a refused charge changes nothing (docs/adr/ADR-023 D1).
        (ToolKind Tool, SkillKind Skill, int Xp)? spend = ChargeOf(plan.Action);
        IProgressCharge? charge = null;
        bool noEnergy = false;
        try
        {
            if (plan.Action == FarmAction.Cleared)
            {
                IUnitOfWork clearing = _unitOfWork.Create();
                await using (clearing.ConfigureAwait(false))
                {
                    IRepository<FarmPlot> plots = clearing.Repository<FarmPlot>();
                    FarmPlot? row = await plots.FirstOrDefaultAsync(p => p.MapId == instance.MapId && p.OwnerId == instance.OwnerId && p.X == x && p.Y == y, cancellationToken).ConfigureAwait(false);
                    if (row is not null)
                    {
                        plots.Remove(row);
                    }

                    this.Audit(clearing, member, plan);
                    charge = await this.ChargeAsync(clearing, playerId, spend, cancellationToken).ConfigureAwait(false);
                    if (charge is { Allowed: false })
                    {
                        return (FarmStatus.NoEnergy, null);
                    }

                    if (!await SaveAsync(clearing, cancellationToken).ConfigureAwait(false))
                    {
                        return (FarmStatus.Conflict, null);
                    }
                }

                await PublishAsync(charge, cancellationToken).ConfigureAwait(false);
                return (FarmStatus.Ok, null);
            }

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
                        charge = await this.ChargeAsync(unit, playerId, spend, ct).ConfigureAwait(false);
                        noEnergy = charge is { Allowed: false };
                        return !noEnergy;
                    },
                    cancellationToken).ConfigureAwait(false);
                if (noEnergy)
                {
                    return (FarmStatus.NoEnergy, null);
                }

                if (change.IsOk)
                {
                    await PublishAsync(charge, cancellationToken).ConfigureAwait(false);
                }

                return (ToStatus(change.Status), null);
            }

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                await this.UpsertAsync(unit, playerId, instance, x, y, plan.Next, cancellationToken).ConfigureAwait(false);
                this.Audit(unit, member, plan);
                PendingGroundDrop? pending = drop is { } staged ? _groundItems.Stage(unit, staged) : null;
                charge = await this.ChargeAsync(unit, playerId, spend, cancellationToken).ConfigureAwait(false);
                if (charge is { Allowed: false })
                {
                    return (FarmStatus.NoEnergy, null);
                }

                if (!await SaveAsync(unit, cancellationToken).ConfigureAwait(false))
                {
                    return (FarmStatus.Conflict, null);
                }

                await PublishAsync(charge, cancellationToken).ConfigureAwait(false);
                return (FarmStatus.Ok, pending);
            }
        }
        finally
        {
            charge?.Dispose();
        }
    }

    // What a farm action costs and earns: planting is free, the scythe's harvest costs nothing but earns farming experience.
    private static (ToolKind Tool, SkillKind Skill, int Xp)? ChargeOf(FarmAction action) => action switch
    {
        FarmAction.Tilled => (ToolKind.Hoe, SkillKind.Farming, SkillRules.TillXp),
        FarmAction.Watered => (ToolKind.WateringCan, SkillKind.Farming, SkillRules.WaterXp),
        FarmAction.Cleared => (ToolKind.Pickaxe, SkillKind.Mining, SkillRules.ClearXp),
        FarmAction.Harvested => (ToolKind.Scythe, SkillKind.Farming, SkillRules.HarvestXp),
        _ => null,
    };

    private async Task<IProgressCharge?> ChargeAsync(IUnitOfWork unit, Guid playerId, (ToolKind Tool, SkillKind Skill, int Xp)? spend, CancellationToken cancellationToken)
        => spend is { } s ? await _progress.ChargeAsync(unit, playerId, s.Tool, s.Skill, s.Xp, cancellationToken).ConfigureAwait(false) : null;

    private static ValueTask PublishAsync(IProgressCharge? charge, CancellationToken cancellationToken)
        => charge is null ? ValueTask.CompletedTask : charge.PublishAsync(cancellationToken);

    // Saves a unit that carries a charge; a concurrent first write of the player's progress row is a conflict, not a crash.
    private static async Task<bool> SaveAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        try
        {
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
        {
            return false;
        }
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

    private async Task PushClearedAsync(MapInstance instance, int x, int y, CancellationToken cancellationToken)
    {
        using PacketScope<FarmTilesCleared> lease = PacketFactory<FarmTilesCleared>.Acquire();
        FarmTilesCleared packet = lease.Value;
        packet.MapId = instance.MapId;
        packet.Tiles.Clear();
        TileRow tile = TileRow.Create();
        tile.X = (ushort)x;
        tile.Y = (ushort)y;
        packet.Tiles.Add(tile);
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

    private readonly record struct PlannedDrop(int ItemId, int Quantity);

    private readonly record struct Plan(FarmVerdict Verdict, FarmAction Action, FarmTile Next, IReadOnlyList<InventoryOperation> Operations, string Reason, QuestEvent? Report, int AuditItemId = 0, int AuditDelta = 0, PlannedDrop? Drop = null)
    {
        public static Plan Refuse(FarmVerdict verdict) => new(verdict, FarmAction.None, default, [], string.Empty, null);
    }
}
