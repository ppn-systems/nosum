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
using Nosum.Abstractions.Visits;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
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
/// only tidies up: it drops reverted tiles from memory and PostgreSQL and tells the players who can see them.
/// The tiles of an instance are kept in per-chunk buckets; a change is published to the subscribers of the chunk it happened in and a chunk's tiles are sent once when it joins a player's interest set (docs/adr/ADR-022).
/// </summary>
public sealed partial class FarmService : IFarmService, IChunkStateContributor, IFarmTiles
{
    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly IChunkPublisher _publisher;
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
    private readonly IGuestbookService? _guestbook;
    private readonly ConcurrentDictionary<string, Task<MapFarm>> _farms = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _mapLocks = new(16);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; the action uses the server's own record of the player's position.</param>
    /// <param name="chunks">Who is interested in which chunk; a read returns only the tiles in the player's interest set.</param>
    /// <param name="publisher">Delivers a tile change to the players who can see its chunk.</param>
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
    /// <param name="guestbook">Counts a visitor's watering into the owner's guestbook (docs/adr/ADR-066); <see langword="null"/> logs nothing.</param>
    public FarmService(
        IMapPresence presence,
        IChunkMap chunks,
        IChunkPublisher publisher,
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
        IPlacedLookup? placed = null,
        IGuestbookService? guestbook = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(publisher);
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
        _chunks = chunks;
        _publisher = publisher;
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
        _guestbook = guestbook;
        if (dayClock is not null)
        {
            dayClock.DayEnded += this.OnDayEnded;
        }
    }

    /// <inheritdoc/>
    public Task<FarmUseOutcome> UseAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, CancellationToken cancellationToken = default)
        => this.UseCoreAsync(connection, hotbarSlot, tileX, tileY, tail: false, cancellationToken);

    // One tile. The tail of a line (docs/adr/ADR-026 D1, FarmService.Area.cs) is worked under the swing and the reach check of its first tile, so it neither starts
    // a second swing nor has to be within arm's length itself.
    private async Task<FarmUseOutcome> UseCoreAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, bool tail, CancellationToken cancellationToken)
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

        if (!tail && !FarmRules.InReach(feetX, feetY, tileX, tileY, map.Grid.TileSize))
        {
            return Refused(FarmStatus.OutOfReach);
        }

        Guid playerId = player.AccountId;
        InventorySlotView held = await _inventory.PeekSlotAsync(playerId, hotbarSlot, cancellationToken).ConfigureAwait(false);
        _ = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? heldItem);

        // One action per interval, whatever is held: a tool swing, a seed, a harvest or bare hands (docs/adr/ADR-021 D8, docs/adr/ADR-072 D2).
        if (!tail && !_tools.TryBeginSwing(playerId))
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
                    worked = farm.TryGet(tileX, tileY, out _);
                }
            }
        }

        if (await _nodes.TryUseAsync(new NodeUseRequest(player, map, hotbarSlot, held, tileX, tileY, worked), cancellationToken).ConfigureAwait(false) is { } handled)
        {
            if (tail && handled.Status != FarmStatus.Blocked)
            {
                return Refused(FarmStatus.Blocked);
            }

            if (handled is { Status: FarmStatus.Ok, Action: FarmAction.Broke })
            {
                await this.ReportAsync(playerId, new QuestEvent(QuestEventKind.NodeBroken, handled.Subject), cancellationToken).ConfigureAwait(false);
            }

            return handled;
        }

        if (map.Tillable is not { } tillable || farm is null)
        {
            return Refused(FarmStatus.NotOnFarm);
        }

        FarmTile changedTile;
        FarmAction action;
        QuestEvent? report;
        int harvestedItem = 0;
        int harvestedQuantity = 0;
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
            // Nothing is worked under a placed piece, whether soil, a crop or bare ground: a crop that ends up beneath furniture cannot be picked (docs/adr/ADR-024 D3).
            if (_placed?.IsCovered(player.Instance, tileX, tileY) == true)
            {
                return Refused(FarmStatus.Blocked);
            }

            FarmTile? current = this.LiveTile(farm, player.Instance, tileX, tileY, nowMs);

            // Ground the co-op has not unlocked cannot be tilled yet, though it is walkable (docs/adr/ADR-038 D4); soil tilled before stays workable.
            bool ground = tillable.IsTillable(tileX, tileY);
            bool locked = ground && member is not null && !CoopUpgrades.IsLandUnlocked(member.LandLevel, tileX, tileY);
            int harvestBonus = FarmRules.IsBonusHarvest(player.Instance.Key, tileX, tileY, nowMs, _calendar, player.Instance.IsOutdoors) ? 1 : 0;
            Plan plan = this.PlanAction(held, current, ground && !locked, nowMs, player.Instance.IsOutdoors, _placed?.SprinklerWateredSinceMs(player.Instance, tileX, tileY), harvestBonus, tail);
            if (plan.Verdict != FarmVerdict.Ok)
            {
                return Refused(locked && plan.Verdict == FarmVerdict.NotTillable ? FarmStatus.LockedLand : ToStatus(plan.Verdict));
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
                _ = farm.Remove(tileX, tileY);
            }
            else
            {
                farm.Set(tileX, tileY, plan.Next);
            }

            changedTile = plan.Next;
            action = plan.Action;
            report = plan.Report;

            // A harvest on co-op land names the crop and its count, so the handler can credit the co-op whose land it was (docs/adr/ADR-067 D2). The membership is
            // already verified above, so no lookup is added to a harvest anywhere else.
            if (action == FarmAction.Harvested && member is not null)
            {
                harvestedItem = plan.AuditItemId;
                harvestedQuantity = plan.AuditDelta;
            }
        }

        if (action == FarmAction.Watered)
        {
            _ = await _tools.UseWaterAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (_guestbook is not null && GuestbookRules.Logs(player.Instance, playerId, this.GuestbookStands(player.Instance)))
            {
                await LogWateringAsync(_guestbook, playerId, player.Instance, _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds()), cancellationToken).ConfigureAwait(false);
            }
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
        return new FarmUseOutcome(FarmStatus.Ok, action, harvestedItem, harvestedQuantity);
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
    private Plan PlanAction(InventorySlotView held, FarmTile? current, bool tillable, long nowMs, bool outdoors, long? sprinklerSinceMs, int harvestBonus, bool tail = false)
    {
        bool hasItem = held.ItemId != 0 && ItemCatalog.TryGet(held.ItemId, out _);
        _ = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item);

        // A mature crop is harvested whatever is held (bare hands included), except on the trailing tiles of a wide tool swing.
        bool withered = current is { HasCrop: true } standing && FarmRules.IsWithered(standing, nowMs, _calendar);
        if (!tail && current is { HasCrop: true } crop && !withered && FarmRules.IsMature(crop, nowMs, _calendar, outdoors, sprinklerSinceMs))
        {
            FarmVerdict harvest = FarmRules.Harvest(current, nowMs, _calendar, out FarmTile bare, out CropDefinition? harvested, outdoors, sprinklerSinceMs);
            if (harvest != FarmVerdict.Ok)
            {
                return Plan.Refuse(harvest);
            }

            // One more unit on a rainy harvest (docs/adr/ADR-027 D8); the audit and the drop count the same units.
            int quantity = harvested!.HarvestQuantity + harvestBonus;
            return new Plan(FarmVerdict.Ok, FarmAction.Harvested, bare, [], "farm.harvest", new QuestEvent(QuestEventKind.CropHarvested, harvested.Id), harvested.HarvestItemId, quantity, new PlannedDrop(harvested.HarvestItemId, quantity));
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
                    return till == FarmVerdict.Ok ? new Plan(till, FarmAction.Tilled, tilled, [], "farm.till", new QuestEvent(QuestEventKind.SoilTilled), item.Id, 0) : Plan.Refuse(till);
                }

            case ItemKind.Tool when item.Tool == ToolKind.WateringCan:
                {
                    // Soil that is wet already (from the can today, a rainy day outdoors or a sprinkler) takes no more water: the can keeps its water and the player their energy.
                    if (current is { } wet && FarmRules.IsWet(wet, nowMs, _calendar, outdoors, sprinklerSinceMs))
                    {
                        return Plan.Refuse(FarmVerdict.NothingToDo);
                    }

                    FarmVerdict water = FarmRules.Water(current, nowMs, _calendar, out FarmTile watered, outdoors, sprinklerSinceMs);
                    QuestEvent? quest = current is { HasCrop: true } && !withered ? new QuestEvent(QuestEventKind.CropWatered) : null;
                    return water == FarmVerdict.Ok ? new Plan(water, FarmAction.Watered, watered, [], "farm.water", quest, item.Id, 0) : Plan.Refuse(water);
                }

            case ItemKind.Tool when item.Tool == ToolKind.Pickaxe:
                {
                    FarmVerdict clear = withered ? FarmVerdict.Ok : FarmRules.Clear(current);
                    return clear == FarmVerdict.Ok ? new Plan(clear, FarmAction.Cleared, default, [], "farm.clear", null, item.Id, 0) : Plan.Refuse(clear);
                }

            case ItemKind.Tool when item.Tool == ToolKind.Scythe && withered:
                {
                    // The dead plant goes; the tilled soil under it stays (docs/adr/ADR-018).
                    FarmVerdict cut = FarmRules.CutWithered(current, nowMs, _calendar, out FarmTile bare);
                    return cut == FarmVerdict.Ok ? new Plan(cut, FarmAction.CutWithered, bare, [], "farm.cut", null, item.Id, 0) : Plan.Refuse(cut);
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
        FarmAction.CutWithered => (ToolKind.Scythe, SkillKind.Farming, SkillRules.ClearXp),
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

    private async Task PushTileAsync(MapInstance instance, int x, int y, FarmTile tile, CancellationToken cancellationToken)
    {
        using PacketScope<FarmTilesChanged> lease = PacketFactory<FarmTilesChanged>.Acquire();
        FarmTilesChanged packet = lease.Value;
        packet.MapId = instance.MapId;
        packet.ServerNowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        packet.Tiles.Clear();
        packet.Tiles.Add(ToRow(x, y, tile));
        await _publisher.PublishAsync(instance, ChunkCoord.OfTile(x, y), packet, null, cancellationToken).ConfigureAwait(false);
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
        await _publisher.PublishAsync(instance, ChunkCoord.OfTile(x, y), packet, null, cancellationToken).ConfigureAwait(false);
    }

    private bool GuestbookStands(MapInstance instance) =>
        _placed is not null && _placed.ItemsOf(instance).Any(piece => PlaceableCatalog.TryGet(piece.ItemId, out PlaceableDefinition? definition) && definition.Kind == PlaceableKind.Guestbook);

    [SuppressMessage("Design", "CA1031", Justification = "The watering is committed; a failing guestbook write must not turn it into an error reply.")]
    private static async Task LogWateringAsync(IGuestbookService guestbook, Guid playerId, MapInstance instance, int day, CancellationToken cancellationToken)
    {
        try
        {
            await guestbook.RecordWateringAsync(instance.OwnerId, playerId, day, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The guestbook is a log: the next watering counts again.
        }
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
}
