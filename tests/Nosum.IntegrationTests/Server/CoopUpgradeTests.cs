// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
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
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Placeable;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
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
/// Co-op upgrades (docs/adr/ADR-038) against a real PostgreSQL database: only the founder buys, a short warehouse changes nothing, a purchase takes its price
/// from the stock in one transaction, grows the capacity and the unlocked land (and both are enforced), every member is told, and a double buy pays once.
/// </summary>
public sealed class CoopUpgradeTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly TmxMapCatalog s_maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly CoopLandTests.Standing _presence = new();
    private readonly CoopMembership _membership;
    private readonly FarmService _farm;
    private readonly CoopService _coops;
    private readonly RecordingNotifier _notifier = new();

    public CoopUpgradeTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _membership = new CoopMembership(_units);
        TransferService transfers = new(_inventory, _clock);
        GroundItemService groundItems = new(_presence, NoChunks.Instance, NoChunks.Instance, s_maps, transfers, _units, _clock);
        _farm = new FarmService(_presence, NoChunks.Instance, NoChunks.Instance, s_maps, _inventory, new QuestService(_units, _clock), _membership, new NoNodes(), new NoCooldownTools(), _units, _clock, GameCalendar.Default, groundItems, new PlayerProgressService(_units, new CalendarGameDayClock(GameCalendar.Default, _clock), _inventory, _clock));
        _coops = new CoopService(_units, transfers, _presence, new EveryoneOnline(), _notifier, new NicknameDirectory(_units), _farm, new CoopLandTests.RecordingEviction(), _clock);
    }

    public void Dispose() => _coops.Dispose();

    [Fact]
    public async Task Upgrade_ANonFounder_IsRefused_AndNothingChanges()
    {
        (_, Guid member, Guid coop) = await CoopWithMemberAsync();
        await StockAsync(coop, (ItemCatalog.Wood, 500), (ItemCatalog.Stone, 500));

        (await _coops.UpgradeAsync(member, CoopUpgradeTrack.Warehouse, 1)).Should().Be(CoopStatus.NotFounder);
        (await _coops.UpgradeAsync(await CreatePlayerAsync(), CoopUpgradeTrack.Land, 1)).Should().Be(CoopStatus.NotInCoop);

        (await LevelsAsync(coop)).Should().Be((0, 0));
        (await StockOfAsync(coop, ItemCatalog.Wood)).Should().Be(500);
        await using NosumDbContext db = new(_database.Options);
        (await db.CoopLog.AnyAsync(e => e.CoopId == coop)).Should().BeFalse();
    }

    [Fact]
    public async Task Upgrade_WithTooLittleStock_ChangesNothing()
    {
        (Guid founder, _, Guid coop) = await CoopWithMemberAsync();
        UpgradeCost[] price = [.. CoopUpgrades.Cost(CoopTrack.Land, 1)];
        await StockAsync(coop, [.. price.Select(c => (c.ItemId, c.Quantity)).Take(price.Length - 1), (price[^1].ItemId, price[^1].Quantity - 1)]);

        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Land, 1)).Should().Be(CoopStatus.NotEnough);

        (await LevelsAsync(coop)).Should().Be((0, 0));
        foreach (UpgradeCost line in price)
        {
            (await StockOfAsync(coop, line.ItemId)).Should().Be(line == price[^1] ? line.Quantity - 1 : line.Quantity, "nothing was taken");
        }

        await using NosumDbContext db = new(_database.Options);
        (await db.CoopLog.AnyAsync(e => e.CoopId == coop)).Should().BeFalse();
    }

    [Fact]
    public async Task Upgrade_Warehouse_PaysFromTheStock_LogsEachItem_AndRaisesTheCapacity()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        await FillWarehouseAsync(coop, CoopUpgrades.WarehouseSlots(0));
        await StockAsync(coop, (ItemCatalog.Wood, 10), (ItemCatalog.Stone, 5));

        // At capacity: a new kind is refused, a kind already held is not.
        int newKind = ItemCatalog.Yam;
        _ = await _inventory.AddAsync(member, newKind, 1, "test.setup");
        _ = await _inventory.AddAsync(member, ItemCatalog.Wood, 5, "test.setup");
        (await _coops.DepositAsync(member, newKind, 1)).Should().Be(CoopStatus.WarehouseFull);
        (await _coops.DepositAsync(member, ItemCatalog.Wood, 5)).Should().Be(CoopStatus.Ok, "wood is already in the warehouse");
        (await StockOfAsync(coop, ItemCatalog.Wood)).Should().Be(16, "1 from filling + 10 + 5 deposited");

        // Short of the price (50 wood, 25 stone): the founder tops the stock up, then buys.
        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Warehouse, 1)).Should().Be(CoopStatus.NotEnough);
        await StockAsync(coop, (ItemCatalog.Wood, 60), (ItemCatalog.Stone, 30));
        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Warehouse, 1)).Should().Be(CoopStatus.Ok);

        (await LevelsAsync(coop)).Should().Be((1, 0));
        (await StockOfAsync(coop, ItemCatalog.Wood)).Should().Be(16 + 60 - 50);
        (await StockOfAsync(coop, ItemCatalog.Stone)).Should().Be(1 + 5 + 30 - 25);
        (await _coops.DepositAsync(member, newKind, 1)).Should().Be(CoopStatus.Ok, "level 1 holds more kinds");

        await using NosumDbContext db = new(_database.Options);
        Guid founderMember = (await db.CoopMembers.SingleAsync(m => m.PlayerId == founder)).Id;
        List<CoopLogEntry> paid = await db.CoopLog.Where(e => e.CoopId == coop && e.Reason == CoopUpgrades.WarehouseReason).ToListAsync();
        paid.Select(e => (e.MemberId, e.ItemId, e.Delta)).Should().BeEquivalentTo(
            CoopUpgrades.Cost(CoopTrack.Warehouse, 1).Select(c => (founderMember, c.ItemId, -c.Quantity)));
        CoopView view = await _coops.GetAsync(member);
        view.WarehouseLevel.Should().Be(1);
        view.Log.Should().HaveCount(2, "the two deposits of the test; the upgrade lines are persisted but not shown in the member log");
        view.Log.Should().OnlyContain(l => l.Delta > 0);
    }

    [Fact]
    public async Task Upgrade_Land_UnlocksTillingForEveryMember_AndLockedGroundIsRefusedBefore()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        (int lockedX, int lockedY) = (25, 45);
        (int eastX, int eastY) = (65, 30);
        (int openX, int openY) = (25, 24);

        Stand(member, coop, openX, openY);
        (await UseHoeAsync(member, openX, openY)).Status.Should().Be(FarmStatus.Ok, "the starting area is open");
        Stand(member, coop, lockedX, lockedY);
        (await UseHoeAsync(member, lockedX, lockedY)).Status.Should().Be(FarmStatus.LockedLand);
        Stand(member, coop, eastX, eastY);
        (await UseHoeAsync(member, eastX, eastY)).Status.Should().Be(FarmStatus.LockedLand);

        await StockAsync(coop, [.. CoopUpgrades.Cost(CoopTrack.Land, 1).Select(c => (c.ItemId, c.Quantity))]);
        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Land, 1)).Should().Be(CoopStatus.Ok);

        Stand(member, coop, lockedX, lockedY);
        (await UseHoeAsync(member, lockedX, lockedY)).Status.Should().Be(FarmStatus.Ok, "the next block is open for every member at once");
        Stand(member, coop, eastX, eastY);
        (await UseHoeAsync(member, eastX, eastY)).Status.Should().Be(FarmStatus.LockedLand, "the east strip needs level 2");

        await StockAsync(coop, [.. CoopUpgrades.Cost(CoopTrack.Land, 2).Select(c => (c.ItemId, c.Quantity))]);
        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Land, 2)).Should().Be(CoopStatus.Ok);
        (await UseHoeAsync(member, eastX, eastY)).Status.Should().Be(FarmStatus.Ok);

        await using NosumDbContext db = new(_database.Options);
        (await db.FarmPlots.CountAsync(p => p.OwnerId == coop)).Should().Be(3);
        (await db.CoopLog.CountAsync(e => e.CoopId == coop && e.Reason == CoopUpgrades.LandReason)).Should().Be(CoopUpgrades.Cost(CoopTrack.Land, 1).Count + CoopUpgrades.Cost(CoopTrack.Land, 2).Count);
    }

    [Fact]
    public async Task Upgrade_SoilTilledBeforeALevelChange_StaysWorkableOnLockedGround()
    {
        // Ground a co-op tilled before land levels existed keeps working: only new tilling is refused (docs/adr/ADR-038 D4).
        (_, Guid member, Guid coop) = await CoopWithMemberAsync();
        (int x, int y) = (25, 45);
        await using (NosumDbContext db = new(_database.Options))
        {
            db.FarmPlots.Add(new FarmPlot { MapId = GameMaps.CoopFarm, OwnerId = coop, X = x, Y = y, TendedDay = 1 });
            _ = await db.SaveChangesAsync();
        }

        Stand(member, coop, x, y);
        (await UseHoeAsync(member, x, y)).Status.Should().Be(FarmStatus.AlreadyTilled, "the soil exists; it is not refused as locked");
    }

    [Fact]
    public async Task Placing_OnLockedLand_IsRefused_UntilTheLandIsUpgraded_AndAFootprintNeedsEveryTile()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid member = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, member);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        LandRect start = CoopUpgrades.LandStep(0);
        int x = 30;
        int edge = start.Y + start.Height - 1;
        world.Map(GameMaps.CoopFarm).Grid.IsBlocked(x, edge).Should().BeFalse();
        world.Map(GameMaps.CoopFarm).Grid.IsBlocked(x, edge + 1).Should().BeFalse("locked ground is walkable");

        // The last row of the starting area is open, the row below it is locked (both walkable).
        world.Stand(member, land, x + 1, edge);
        int slot = await world.GiveAsync(member, ItemCatalog.Chest, 3);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(member), slot, x, edge + 1, 0)).Status.Should().Be(PlaceStatus.LockedLand);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(member), slot, x, edge, 0)).Status.Should().Be(PlaceStatus.Ok);
        int placed = (await world.Placeables.GetAsync(world.Presence.ConnectionOf(member))).Objects.Single().Id;
        (await world.Placeables.MoveAsync(world.Presence.ConnectionOf(member), placed, x, edge + 1, 0)).Status.Should().Be(PlaceStatus.LockedLand, "moving onto locked ground is refused too");

        await using (NosumDbContext db = world.Db())
        {
            Coop row = await db.Coops.SingleAsync(c => c.Id == coop);
            row.LandLevel = 1;
            _ = await db.SaveChangesAsync();
        }

        (await world.Placeables.MoveAsync(world.Presence.ConnectionOf(member), placed, x, edge + 1, 0)).Status.Should().Be(PlaceStatus.Ok);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(member), slot, x + 2, edge + 1, 0)).Status.Should().Be(PlaceStatus.Ok);
    }

    [Fact]
    public async Task Upgrade_TwoRequestsForTheSameLevelAtOnce_PayOnce()
    {
        (Guid founder, _, Guid coop) = await CoopWithMemberAsync();
        UpgradeCost[] price = [.. CoopUpgrades.Cost(CoopTrack.Land, 1)];
        await StockAsync(coop, [.. price.Select(c => (c.ItemId, c.Quantity * 2))]);

        CoopStatus[] results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => _coops.UpgradeAsync(founder, CoopUpgradeTrack.Land, 1))));

        results.Count(r => r == CoopStatus.Ok).Should().Be(1);
        results.Where(r => r != CoopStatus.Ok).Should().OnlyContain(r => r == CoopStatus.AlreadyUpgraded);
        (await LevelsAsync(coop)).Should().Be((0, 1));
        foreach (UpgradeCost line in price)
        {
            (await StockOfAsync(coop, line.ItemId)).Should().Be(line.Quantity, "the price was taken once, not twice");
        }

        await using NosumDbContext db = new(_database.Options);
        (await db.CoopLog.CountAsync(e => e.CoopId == coop && e.Reason == CoopUpgrades.LandReason)).Should().Be(price.Length);
    }

    [Fact]
    public async Task Upgrade_ASkippedLevel_OrATrackAtItsMaximum_IsRefused()
    {
        (Guid founder, _, Guid coop) = await CoopWithMemberAsync();
        await StockAsync(coop, (ItemCatalog.Wood, 5000), (ItemCatalog.Stone, 5000), (ItemCatalog.CopperBar, 100), (ItemCatalog.IronBar, 100));

        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Warehouse, 2)).Should().Be(CoopStatus.AlreadyUpgraded, "level 1 comes first");
        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Warehouse, 0)).Should().Be(CoopStatus.AlreadyUpgraded);
        (await _coops.UpgradeAsync(founder, (CoopUpgradeTrack)9, 1)).Should().Be(CoopStatus.AlreadyUpgraded);
        for (int level = 1; level <= CoopUpgrades.MaxLevel; level++)
        {
            (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Warehouse, level)).Should().Be(CoopStatus.Ok);
        }

        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Warehouse, CoopUpgrades.MaxLevel + 1)).Should().Be(CoopStatus.MaxLevel);
        (await LevelsAsync(coop)).Should().Be((CoopUpgrades.MaxLevel, 0));
    }

    [Fact]
    public async Task Upgrade_EveryOnlineMember_ReceivesTheNewLevel()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        await StockAsync(coop, [.. CoopUpgrades.Cost(CoopTrack.Land, 1).Select(c => (c.ItemId, c.Quantity))]);
        _notifier.Sent.Clear();

        (await _coops.UpgradeAsync(founder, CoopUpgradeTrack.Land, 1)).Should().Be(CoopStatus.Ok);

        _notifier.Sent.Select(s => s.Player).Should().BeEquivalentTo([founder, member]);
        _notifier.Sent.Should().OnlyContain(s => s.LandLevel == 1 && s.WarehouseLevel == 0);
        (await _coops.GetAsync(member)).LandLevel.Should().Be(1);
    }

    private async Task<FarmUseOutcome> UseHoeAsync(Guid player, int x, int y)
    {
        int slot = (await _inventory.GetAsync(player)).Slots.First(s => s.ItemId == ItemCatalog.Hoe).Slot;
        return await _farm.UseAsync(_presence.ConnectionOf(player), slot, x, y);
    }

    private void Stand(Guid player, Guid coop, int tileX, int tileY)
        => _presence.Stand(player, MapInstance.OfCoop(GameMaps.CoopFarm, coop), (tileX * 16) + 8, (tileY * 16) + 8);

    private async Task<(int Warehouse, int Land)> LevelsAsync(Guid coop)
    {
        await using NosumDbContext db = new(_database.Options);
        Coop row = await db.Coops.AsNoTracking().SingleAsync(c => c.Id == coop);
        return (row.WarehouseLevel, row.LandLevel);
    }

    private async Task<int> StockOfAsync(Guid coop, int itemId)
    {
        await using NosumDbContext db = new(_database.Options);
        return (await db.CoopStock.AsNoTracking().FirstOrDefaultAsync(s => s.CoopId == coop && s.ItemId == itemId))?.Quantity ?? 0;
    }

    // Puts items straight into the warehouse (adds to what is there), as if members had deposited them.
    private async Task StockAsync(Guid coop, params (int ItemId, int Quantity)[] items)
    {
        await using NosumDbContext db = new(_database.Options);
        foreach ((int itemId, int quantity) in items)
        {
            CoopStockItem? row = await db.CoopStock.FirstOrDefaultAsync(s => s.CoopId == coop && s.ItemId == itemId);
            if (row is null)
            {
                db.CoopStock.Add(new CoopStockItem { CoopId = coop, ItemId = itemId, Quantity = quantity });
            }
            else
            {
                row.Quantity += quantity;
            }
        }

        _ = await db.SaveChangesAsync();
    }

    // Fills the warehouse with distinct tradable item kinds (crops first) until it holds exactly `kinds` kinds, wood and stone among them.
    private async Task FillWarehouseAsync(Guid coop, int kinds)
    {
        List<int> ids = [ItemCatalog.Wood, ItemCatalog.Stone];
        ids.AddRange(Enumerable.Range(ItemCatalog.Parsnip, 30).Where(i => i != ItemCatalog.Yam && ItemCatalog.TryGet(i, out _)));
        await StockAsync(coop, [.. ids.Take(kinds).Select(i => (i, 1))]);
    }

    private async Task<(Guid Founder, Guid Member, Guid Coop)> CoopWithMemberAsync()
    {
        Guid founder = await CreatePlayerAsync();
        Guid member = await CreatePlayerAsync();
        (await _coops.CreateAsync(founder, "Upgrading")).Should().Be(CoopStatus.Ok);
        Guid coop = (await _membership.FindAsync(founder))!.CoopId;
        await using (NosumDbContext db = new(_database.Options))
        {
            db.CoopMembers.Add(new CoopMember { CoopId = coop, PlayerId = member, Role = CoopRole.Member, JoinedAt = _clock.GetUtcNow() });
            _ = await db.SaveChangesAsync();
        }

        return (founder, member, coop);
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

    private sealed class EveryoneOnline : ISessionRegistry
    {
        private readonly IConnection _connection = DispatchProxy.Create<IConnection, CoopLandTests.ConnectionStub>();

        public ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Release(IConnection connection)
        {
        }

        public IConnection? GetActive(Guid playerId) => _connection;
    }

    // Records the levels of each snapshot at the moment it is sent (the packet is returned to its pool afterwards).
    private sealed class RecordingNotifier : IPlayerNotifier
    {
        public List<(Guid Player, int WarehouseLevel, int LandLevel)> Sent { get; } = [];

        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
        {
            if (packet is CoopSnapshot snapshot)
            {
                lock (this.Sent)
                {
                    this.Sent.Add((playerId, snapshot.WarehouseLevel, snapshot.LandLevel));
                }
            }

            return ValueTask.FromResult(true);
        }
    }
}
