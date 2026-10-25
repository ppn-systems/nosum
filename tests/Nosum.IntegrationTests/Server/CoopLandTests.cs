// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Farm;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Farm;
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Quests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Farming on a co-op's shared land (docs/adr/ADR-020) against a real PostgreSQL database: members farm and are audited, a non-member and a removed
/// member are refused, one plot is harvested once however many members race for it, and a dissolved co-op's land is wiped.
/// </summary>
public sealed class CoopLandTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly TmxMapCatalog s_maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly Standing _presence = new();
    private readonly RecordingEviction _eviction = new();
    private readonly CoopMembership _membership;
    private readonly FarmService _farm;
    private readonly CoopService _coops;

    public CoopLandTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _membership = new CoopMembership(_units);
        TransferService transfers = new(_inventory, _clock);
        GroundItemService groundItems = new(_presence, NoChunks.Instance, NoChunks.Instance, s_maps, transfers, _units, _clock);
        _farm = new FarmService(_presence, NoChunks.Instance, NoChunks.Instance, s_maps, _inventory, new QuestService(_units, _clock), _membership, new NoNodes(), new NoCooldownTools(), _units, _clock, GameCalendar.Default, groundItems, new PlayerProgressService(_units, new CalendarGameDayClock(GameCalendar.Default, _clock), _inventory, _clock));
        _coops = new CoopService(_units, transfers, _presence, new NoSessions(), new NoNotifier(), new NicknameDirectory(_units), _farm, _eviction, _clock);
    }

    public void Dispose() => _coops.Dispose();

    // The parsnip was planted and watered on game day 1: three more watered days and the fourth rollover make it ripe (docs/adr/ADR-021 D7).
    private async Task GrowParsnipAsync(Guid player, int x, int y)
    {
        for (int day = 2; day <= CropCatalog.All[0].TotalGrowthDays; day++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs));
            (await UseAsync(player, FarmItem.Can, x, y)).Status.Should().Be(FarmStatus.Ok);
        }

        _clock.Advance(TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs));
    }

    [Fact]
    public async Task Farm_MembersWorkTheSameLand_PlantedByOneAndHarvestedByAnother_AuditedAndPaidToTheHarvester()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        (int x, int y) = (25, 24);
        Stand(founder, coop, x, y);
        Stand(member, coop, x, y);

        (await UseAsync(founder, FarmItem.Hoe, x, y)).Should().Be(new FarmUseOutcome(FarmStatus.Ok, FarmAction.Tilled));
        (await UseAsync(founder, FarmItem.Seed, x, y)).Should().Be(new FarmUseOutcome(FarmStatus.Ok, FarmAction.Planted));
        (await UseAsync(member, FarmItem.Can, x, y)).Should().Be(new FarmUseOutcome(FarmStatus.Ok, FarmAction.Watered), "a member may water what another planted");
        (await UseAsync(member, FarmItem.Hoe, x, y)).Status.Should().NotBe(FarmStatus.Ok, "an unripe crop is not harvested");
        await GrowParsnipAsync(member, x, y);
        (await UseAsync(member, FarmItem.Hoe, x, y)).Should().Be(new FarmUseOutcome(FarmStatus.Ok, FarmAction.Harvested), "a member may harvest what another planted");

        (await CountAsync(member, ItemCatalog.Parsnip)).Should().BePositive("the harvester takes the produce");
        (await CountAsync(founder, ItemCatalog.Parsnip)).Should().Be(0);
        (await CountAsync(founder, ItemCatalog.ParsnipSeeds)).Should().Be(7, "the planter's seed was consumed");
        (await CountAsync(member, ItemCatalog.ParsnipSeeds)).Should().Be(8);

        await using NosumDbContext context = new(_database.Options);
        FarmPlot plot = await context.FarmPlots.SingleAsync(p => p.OwnerId == coop);
        (plot.MapId, plot.X, plot.Y, plot.CropId, plot.LastWorkedBy).Should().Be((GameMaps.CoopFarm, x, y, 0, member));
        (await context.FarmPlots.AnyAsync(p => p.OwnerId == founder || p.OwnerId == member)).Should().BeFalse("nothing was written to anyone's private farm");

        MemberIds ids = await IdsAsync(context, coop, founder, member);
        List<CoopLogEntry> audit = await context.CoopLog.Where(e => e.CoopId == coop).OrderBy(e => e.OccurredAt).ThenBy(e => e.Reason).ToListAsync();
        audit.Select(e => (e.Reason, e.MemberId)).Should().BeEquivalentTo(
            new[]
            {
                (CoopLand.TillReason, ids.Founder),
                (CoopLand.PlantReason, ids.Founder),
                (CoopLand.WaterReason, ids.Member),
                (CoopLand.WaterReason, ids.Member),
                (CoopLand.WaterReason, ids.Member),
                (CoopLand.WaterReason, ids.Member),
                (CoopLand.HarvestReason, ids.Member),
            });
        audit.Single(e => e.Reason == CoopLand.PlantReason).Should().Match<CoopLogEntry>(e => e.ItemId == ItemCatalog.ParsnipSeeds && e.Delta == -1);
        audit.Single(e => e.Reason == CoopLand.HarvestReason).Should().Match<CoopLogEntry>(e => e.ItemId == ItemCatalog.Parsnip && e.Delta > 0);
    }

    [Fact]
    public async Task Farm_Harvests_DoNotCountAsWarehouseContributions()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        Stand(member, coop, 26, 24);
        _ = await UseAsync(member, FarmItem.Hoe, 26, 24);
        _ = await UseAsync(member, FarmItem.Seed, 26, 24);
        _ = await UseAsync(member, FarmItem.Can, 26, 24);
        await GrowParsnipAsync(member, 26, 24);
        _ = await UseAsync(member, FarmItem.Hoe, 26, 24);
        _ = await _inventory.AddAsync(founder, ItemCatalog.Radish, 3, "test.setup");
        (await _coops.DepositAsync(founder, ItemCatalog.Radish, 3)).Should().Be(CoopStatus.Ok);

        CoopView view = await _coops.GetAsync(member);

        view.Stock.Single(s => s.ItemId == ItemCatalog.Radish).Available.Should().Be(0, "the member put nothing into the warehouse; the harvest went into their own inventory");
        view.Log.Should().OnlyContain(l => l.ItemId == ItemCatalog.Radish, "the panel's recent log lists warehouse moves only");
    }

    [Fact]
    public async Task Farm_TwoCoops_HaveSeparateLandAtTheSameTiles()
    {
        (Guid founderA, _, Guid coopA) = await CoopWithMemberAsync();
        Guid founderB = await CreatePlayerAsync();
        (await _coops.CreateAsync(founderB, "Second coop")).Should().Be(CoopStatus.Ok);
        Guid coopB = (await _membership.FindAsync(founderB))!.CoopId;
        Stand(founderA, coopA, 25, 25);
        Stand(founderB, coopB, 25, 25);

        (await UseAsync(founderA, FarmItem.Hoe, 25, 25)).Status.Should().Be(FarmStatus.Ok);
        (await UseAsync(founderB, FarmItem.Hoe, 25, 25)).Status.Should().Be(FarmStatus.Ok, "the other co-op's tile at the same place is untouched");
        (await UseAsync(founderA, FarmItem.Hoe, 25, 25)).Status.Should().Be(FarmStatus.AlreadyTilled);

        await using NosumDbContext context = new(_database.Options);
        (await context.FarmPlots.CountAsync(p => p.OwnerId == coopA)).Should().Be(1);
        (await context.FarmPlots.CountAsync(p => p.OwnerId == coopB)).Should().Be(1);
    }

    [Fact]
    public async Task Farm_NonMember_IsRefusedAndNothingIsWritten()
    {
        (_, _, Guid coop) = await CoopWithMemberAsync();
        Guid stranger = await CreatePlayerAsync();
        Stand(stranger, coop, 25, 24);

        FarmUseOutcome outcome = await UseAsync(stranger, FarmItem.Hoe, 25, 24);

        outcome.Should().Be(new FarmUseOutcome(FarmStatus.NotOnFarm, FarmAction.None));
        await using NosumDbContext context = new(_database.Options);
        (await context.FarmPlots.AnyAsync(p => p.OwnerId == coop)).Should().BeFalse();
        (await context.CoopLog.AnyAsync(e => e.CoopId == coop)).Should().BeFalse();
    }

    [Fact]
    public async Task Farm_MemberOfAnotherCoop_CannotWorkThisCoopsLand()
    {
        (_, _, Guid coop) = await CoopWithMemberAsync();
        Guid otherFounder = await CreatePlayerAsync();
        (await _coops.CreateAsync(otherFounder, "Other coop")).Should().Be(CoopStatus.Ok);
        Stand(otherFounder, coop, 25, 24);

        (await UseAsync(otherFounder, FarmItem.Hoe, 25, 24)).Status.Should().Be(FarmStatus.NotOnFarm);
    }

    [Fact]
    public async Task Kick_And_Leave_RevokeFarmingAtOnce_AndMoveThePlayerOffTheLand()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        Guid leaver = await CreatePlayerAsync();
        await AddMemberAsync(coop, leaver);
        Stand(member, coop, 25, 24);
        Stand(leaver, coop, 25, 24);
        (await UseAsync(member, FarmItem.Hoe, 25, 24)).Status.Should().Be(FarmStatus.Ok);
        Guid memberRow = (await _membership.FindAsync(member))!.MemberId;

        (await _coops.KickAsync(founder, memberRow)).Should().Be(CoopStatus.Ok);
        (await _coops.LeaveAsync(leaver)).Should().Be(CoopStatus.Ok);

        _eviction.Evicted.Should().BeEquivalentTo([member, leaver]);
        (await UseAsync(member, FarmItem.Hoe, 26, 24)).Status.Should().Be(FarmStatus.NotOnFarm, "the kicked member is refused even if they were still standing there");
        (await UseAsync(leaver, FarmItem.Hoe, 26, 24)).Status.Should().Be(FarmStatus.NotOnFarm);
        await using NosumDbContext context = new(_database.Options);
        (await context.FarmPlots.CountAsync(p => p.OwnerId == coop)).Should().Be(1, "the land and the work on it stay with the co-op");
    }

    [Fact]
    public async Task Harvest_TwoMembersAtOnce_ExactlyOneWinsEachPlot()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        const int plots = 8;
        for (int i = 0; i < plots; i++)
        {
            Stand(founder, coop, 20 + i, 26);
            Stand(member, coop, 20 + i, 26);
            _ = await UseAsync(founder, FarmItem.Hoe, 20 + i, 26);
            _ = await UseAsync(founder, FarmItem.Seed, 20 + i, 26);
            _ = await UseAsync(founder, FarmItem.Can, 20 + i, 26);
        }

        for (int day = 2; day <= CropCatalog.All[0].TotalGrowthDays; day++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs));
            for (int i = 0; i < plots; i++)
            {
                Stand(founder, coop, 20 + i, 26);
                _ = await UseAsync(founder, FarmItem.Can, 20 + i, 26);
            }
        }

        _clock.Advance(TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs));
        int wins = 0;
        for (int i = 0; i < plots; i++)
        {
            Stand(founder, coop, 20 + i, 26);
            Stand(member, coop, 20 + i, 26);
            int tile = 20 + i;
            FarmUseOutcome[] outcomes = await Task.WhenAll(Task.Run(() => UseAsync(founder, FarmItem.Hoe, tile, 26)), Task.Run(() => UseAsync(member, FarmItem.Hoe, tile, 26)));
            wins += outcomes.Count(o => o.Action == FarmAction.Harvested);
            outcomes.Count(o => o.Action == FarmAction.Harvested).Should().Be(1, "tile " + tile);
            outcomes.Single(o => o.Action != FarmAction.Harvested).Status.Should().NotBe(FarmStatus.Ok);
        }

        wins.Should().Be(plots);
        int harvested = (await CountAsync(founder, ItemCatalog.Parsnip)) + (await CountAsync(member, ItemCatalog.Parsnip));
        _ = CropCatalog.TryGet(CropCatalog.Parsnip, out CropDefinition? parsnip);
        harvested.Should().Be(plots * parsnip!.HarvestQuantity, "every plot paid out exactly once");
        await using NosumDbContext context = new(_database.Options);
        (await context.CoopLog.CountAsync(e => e.CoopId == coop && e.Reason == CoopLand.HarvestReason)).Should().Be(plots);
    }

    [Fact]
    public async Task Dissolve_LoneFounderLeavingAnEmptyCoop_WipesItsLand()
    {
        Guid founder = await CreatePlayerAsync();
        (await _coops.CreateAsync(founder, "Short lived")).Should().Be(CoopStatus.Ok);
        Guid coop = (await _membership.FindAsync(founder))!.CoopId;
        Stand(founder, coop, 25, 24);
        _ = await UseAsync(founder, FarmItem.Hoe, 25, 24);
        await using (NosumDbContext context = new(_database.Options))
        {
            (await context.FarmPlots.CountAsync(p => p.OwnerId == coop)).Should().Be(1);
        }

        (await _coops.LeaveAsync(founder)).Should().Be(CoopStatus.Ok);

        await using NosumDbContext after = new(_database.Options);
        (await after.Coops.AnyAsync(c => c.Id == coop)).Should().BeFalse();
        (await after.FarmPlots.AnyAsync(p => p.OwnerId == coop)).Should().BeFalse("the land goes with the co-op");
        (await after.CoopLog.AnyAsync(e => e.CoopId == coop)).Should().BeFalse();
        _eviction.Evicted.Should().Contain(founder);
    }

    [Fact]
    public async Task Dissolve_ARefusedWhileAChestOrFurnaceOnTheLandHoldsSomething_ThenThePlacedObjectsAreWiped()
    {
        Guid founder = await CreatePlayerAsync();
        (await _coops.CreateAsync(founder, "Short lived")).Should().Be(CoopStatus.Ok);
        Guid coop = (await _membership.FindAsync(founder))!.CoopId;
        int chest;
        int furnace;
        await using (NosumDbContext context = new(_database.Options))
        {
            PlacedObject chestRow = new() { MapId = GameMaps.CoopFarm, OwnerId = coop, ItemId = ItemCatalog.Chest, TileX = 40, TileY = 20, PlacedAt = s_start, UpdatedAt = s_start };
            PlacedObject furnaceRow = new() { MapId = GameMaps.CoopFarm, OwnerId = coop, ItemId = ItemCatalog.Furnace, TileX = 42, TileY = 20, PlacedAt = s_start, UpdatedAt = s_start };
            context.PlacedObjects.AddRange(chestRow, furnaceRow);
            _ = await context.SaveChangesAsync();
            (chest, furnace) = (chestRow.Id, furnaceRow.Id);
            context.FurnaceJobs.Add(new FurnaceJob { ObjectId = furnace, OreItemId = ItemCatalog.CopperOre, StartedAtMs = 1 });
            _ = await context.SaveChangesAsync();
        }

        Guid chestOwner = ContainerIds.ForChest(chest);
        _ = await _inventory.AddAsync(chestOwner, ItemCatalog.Wood, 5, "test.setup");

        (await _coops.LeaveAsync(founder)).Should().Be(CoopStatus.CannotLeave, "the chest holds wood");
        _ = await _inventory.RemoveAsync(chestOwner, ItemCatalog.Wood, 5, "test.setup");
        (await _coops.LeaveAsync(founder)).Should().Be(CoopStatus.CannotLeave, "the furnace holds a job");
        await using (NosumDbContext context = new(_database.Options))
        {
            context.FurnaceJobs.RemoveRange(await context.FurnaceJobs.Where(j => j.ObjectId == furnace).ToListAsync());
            _ = await context.SaveChangesAsync();
        }

        (await _coops.LeaveAsync(founder)).Should().Be(CoopStatus.Ok);

        await using NosumDbContext after = new(_database.Options);
        (await after.Coops.AnyAsync(c => c.Id == coop)).Should().BeFalse();
        (await after.PlacedObjects.AnyAsync(p => p.OwnerId == coop)).Should().BeFalse("the land's objects go with the co-op");
        (await after.PlayerInventories.AnyAsync(i => i.PlayerId == chestOwner)).Should().BeFalse("and so does the empty chest's inventory");
    }

    private enum FarmItem
    {
        Hoe,
        Can,
        Seed,
    }

    private sealed record MemberIds(Guid Founder, Guid Member);

    private static async Task<MemberIds> IdsAsync(NosumDbContext context, Guid coop, Guid founder, Guid member)
        => new(
            (await context.CoopMembers.SingleAsync(m => m.CoopId == coop && m.PlayerId == founder)).Id,
            (await context.CoopMembers.SingleAsync(m => m.CoopId == coop && m.PlayerId == member)).Id);

    private async Task<FarmUseOutcome> UseAsync(Guid player, FarmItem item, int x, int y)
    {
        int itemId = item switch { FarmItem.Hoe => ItemCatalog.Hoe, FarmItem.Can => ItemCatalog.WateringCan, _ => ItemCatalog.ParsnipSeeds };
        int slot = (await _inventory.GetAsync(player)).Slots.First(s => s.ItemId == itemId).Slot;
        return await _farm.UseAsync(_presence.ConnectionOf(player), slot, x, y);
    }

    private async Task<int> CountAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    private void Stand(Guid player, Guid coop, int tileX, int tileY)
        => _presence.Stand(player, MapInstance.OfCoop(GameMaps.CoopFarm, coop), (tileX * 16) + 8, (tileY * 16) + 8);

    private async Task<(Guid Founder, Guid Member, Guid Coop)> CoopWithMemberAsync()
    {
        Guid founder = await CreatePlayerAsync();
        Guid member = await CreatePlayerAsync();
        (await _coops.CreateAsync(founder, "Shared fields")).Should().Be(CoopStatus.Ok);
        Guid coop = (await _membership.FindAsync(founder))!.CoopId;
        await AddMemberAsync(coop, member);
        return (founder, member, coop);
    }

    private async Task AddMemberAsync(Guid coop, Guid player)
    {
        await using NosumDbContext context = new(_database.Options);
        context.CoopMembers.Add(new CoopMember { CoopId = coop, PlayerId = player, Role = CoopRole.Member, JoinedAt = _clock.GetUtcNow() });
        _ = await context.SaveChangesAsync();
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    // A connection nobody talks to: presence only stores it.
    internal class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    internal sealed class RecordingEviction : ICoopLandEviction
    {
        private readonly List<Guid> _evicted = [];

        public IReadOnlyList<Guid> Evicted
        {
            get
            {
                lock (_evicted)
                {
                    return [.. _evicted];
                }
            }
        }

        public ValueTask EvictAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            lock (_evicted)
            {
                _evicted.Add(accountId);
            }

            return ValueTask.CompletedTask;
        }
    }

    internal sealed class NoNotifier : IPlayerNotifier
    {
        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    internal sealed class NoSessions : ISessionRegistry
    {
        public ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Release(IConnection connection)
        {
        }

        public ValueTask<bool> EndAsync(Guid playerId, Nosum.Contracts.Account.AccountStatus reason, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public IConnection? GetActive(Guid playerId) => null;
    }

    // Reports each registered player at a chosen place of a chosen instance; everything else is empty.
    internal sealed class Standing : IMapPresence
    {
        private readonly Dictionary<Guid, (IConnection Connection, PlayerPresence Presence)> _byPlayer = [];
        private readonly Dictionary<IConnection, PlayerPresence> _byConnection = [];

        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => _byPlayer.Count;

        public IConnection ConnectionOf(Guid player) => _byPlayer[player].Connection;

        [SuppressMessage("Reliability", "CA2000", Justification = "A stub connection that holds nothing to dispose.")]
        public void Stand(Guid account, MapInstance instance, double x, double y)
        {
            lock (_byPlayer)
            {
                IConnection connection = _byPlayer.TryGetValue(account, out (IConnection Connection, PlayerPresence Presence) known) ? known.Connection : DispatchProxy.Create<IConnection, ConnectionStub>();
                PlayerPresence presence = new(1, account, instance, connection, new MotionState(x, y, 0));
                _byPlayer[account] = (connection, presence);
                _byConnection[connection] = presence;
            }
        }

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
        {
            lock (_byPlayer)
            {
                return _byConnection.TryGetValue(connection, out player);
            }
        }

        public void CopyActiveMapIds(List<string> into)
        {
        }

        public void CopyPlayers(string instanceKey, List<PlayerPresence> into)
        {
        }

        public ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask MulticastAsync(string instanceKey, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
