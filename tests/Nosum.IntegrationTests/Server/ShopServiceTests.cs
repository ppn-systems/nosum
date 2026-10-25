// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Shop;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Calendar;
using Nosum.Domain.Economy;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Dev;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Shops;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Buying at a shop against a real PostgreSQL database: coins and items move together or not at all (docs/adr/ADR-013 amendments).</summary>
public sealed class ShopServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly StandingPresence _presence = new();
    private readonly ShopService _shop;
    private readonly IConnection _connection = DispatchProxy.Create<IConnection, ConnectionStub>();

    public ShopServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _shop = new ShopService(_transfers, _presence, new TownCatalog(), GameCalendar.Default, TimeProvider.System);
    }

    [Fact]
    public async Task Buy_AtTheCounter_TakesCoinsAndGivesItemsInOneTransactionWithAuditRows()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.FurnitureShop);

        ShopOutcome outcome = await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 2);

        outcome.Should().Be(new ShopOutcome(ShopStatus.Ok, 200));
        (await _transfers.GetBalanceAsync(player)).Should().Be(300);
        (await CountAsync(player, ItemCatalog.OakChair)).Should().Be(2);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == "shop.buy").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(-200);
        (await context.InventoryLog.Where(e => e.PlayerId == player && e.Reason == "shop.buy").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(2);
    }

    [Fact]
    public async Task Buy_NotEnoughCoins_ChangesNothing()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.FurnitureShop);

        // 500 coins, a couch is 400 each: the second one cannot be paid.
        (await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.BlueCouch, 1)).Status.Should().Be(ShopStatus.Ok);
        ShopOutcome refused = await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.BlueCouch, 1);

        refused.Should().Be(new ShopOutcome(ShopStatus.NotEnoughCoins, 0));
        (await _transfers.GetBalanceAsync(player)).Should().Be(100);
        (await CountAsync(player, ItemCatalog.BlueCouch)).Should().Be(1);
    }

    [Fact]
    public async Task Buy_FullInventory_IsRefusedAndNoCoinIsTaken()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.FurnitureShop);
        _ = await _inventory.AddAsync(player, ItemCatalog.ShirtRed, InventoryLimits.DefaultCapacity - StarterKit.Items.Length, "test.fill"); // fill every slot after the starter kit

        ShopOutcome refused = await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 1);

        refused.Should().Be(new ShopOutcome(ShopStatus.NoSpace, 0));
        (await _transfers.GetBalanceAsync(player)).Should().Be(500);
        (await CountAsync(player, ItemCatalog.OakChair)).Should().Be(0);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoinLog.AnyAsync(e => e.PlayerId == player && e.Reason == "shop.buy")).Should().BeFalse();
    }

    [Fact]
    public async Task Buy_ASeedStackThatStillFitsAnExistingStack_Works()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.SeedShop);
        _ = await _inventory.AddAsync(player, ItemCatalog.ShirtRed, InventoryLimits.DefaultCapacity - StarterKit.Items.Length, "test.fill");

        // Parsnip seeds merge into the starter stack (8 + 10 <= 99), so a full inventory still takes them.
        (await _shop.BuyAsync(_connection, player, ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 10)).Status.Should().Be(ShopStatus.Ok);

        (await CountAsync(player, ItemCatalog.ParsnipSeeds)).Should().Be(18);
        (await _transfers.GetBalanceAsync(player)).Should().Be(400);
    }

    [Fact]
    public async Task Buy_FarFromTheCounterOrOnAnotherMap_IsRefused()
    {
        Guid player = await CreatePlayerAsync();
        _presence.Stand(player, GameMaps.Town, 10 * 16, 10 * 16);
        (await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 1)).Status.Should().Be(ShopStatus.NotAtShop);

        _presence.Stand(player, GameMaps.BusStop, (43 * 16) + 8, (57 * 16) + 8);
        (await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 1)).Status.Should().Be(ShopStatus.NotAtShop);

        _presence.Remove();
        (await _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 1)).Status.Should().Be(ShopStatus.NotAtShop, "not on any map");
        (await _transfers.GetBalanceAsync(player)).Should().Be(500);
    }

    [Theory]
    [InlineData(99, ItemCatalog.OakChair, 1, ShopStatus.UnknownShop)]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.Hoe, 1, ShopStatus.NotSold)]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.Parsnip, 1, ShopStatus.NotSold)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 0, ShopStatus.InvalidQuantity)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, -5, ShopStatus.InvalidQuantity)]
    [InlineData(ShopCatalog.ClothingShop, ItemCatalog.ShirtRed, 2, ShopStatus.InvalidQuantity)]
    public async Task Buy_InvalidRequest_IsRefusedBeforeAnythingMoves(int shopId, int itemId, int quantity, ShopStatus expected)
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.FurnitureShop);

        (await _shop.BuyAsync(_connection, player, shopId, itemId, quantity)).Status.Should().Be(expected);

        (await _transfers.GetBalanceAsync(player)).Should().Be(500);
    }

    [Fact]
    public async Task Buy_ManyAtOnceWithCoinsForOnlySome_NeverSpendsMoreThanTheBalance()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.FurnitureShop);

        // Each lamp is 140: with 500 coins exactly three of the ten parallel purchases can succeed.
        ShopOutcome[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _shop.BuyAsync(_connection, player, ShopCatalog.FurnitureShop, ItemCatalog.FloorLamp, 1)));

        results.Count(r => r.Status == ShopStatus.Ok).Should().Be(3);
        results.Where(r => r.Status != ShopStatus.Ok).Select(r => r.Status).Should().OnlyContain(status => status == ShopStatus.NotEnoughCoins || status == ShopStatus.Conflict);
        (await _transfers.GetBalanceAsync(player)).Should().Be(500 - (3 * 140));
        (await CountAsync(player, ItemCatalog.FloorLamp)).Should().Be(3);
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.CauliflowerSeeds, 30)]
    [InlineData(ShopCatalog.ClothingShop, ItemCatalog.HatCap, 40)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.HousePlant, 80)]
    public async Task Buy_AtEachShopsOwnCounter_SellsThatShopsItems(int shopId, int itemId, int price)
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, shopId);

        (await _shop.BuyAsync(_connection, player, shopId, itemId, 1)).Should().Be(new ShopOutcome(ShopStatus.Ok, price));

        (await _transfers.GetBalanceAsync(player)).Should().Be(500 - price);
        (await CountAsync(player, itemId)).Should().BeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.OakChair)]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.ShirtBlue)]
    [InlineData(ShopCatalog.ClothingShop, ItemCatalog.ParsnipSeeds)]
    [InlineData(ShopCatalog.ClothingShop, ItemCatalog.FloorLamp)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.RadishSeeds)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.PantsKhaki)]
    public async Task Buy_AnItemTheShopDoesNotSell_IsRefusedEvenAtItsOwnCounter_AndNothingMoves(int shopId, int itemId)
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, shopId);
        int before = await CountAsync(player, itemId);

        (await _shop.BuyAsync(_connection, player, shopId, itemId, 1)).Should().Be(new ShopOutcome(ShopStatus.NotSold, 0));

        (await _transfers.GetBalanceAsync(player)).Should().Be(500);
        (await CountAsync(player, itemId)).Should().Be(before);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoinLog.AnyAsync(e => e.PlayerId == player && e.Reason == "shop.buy")).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop, ShopCatalog.FurnitureShop, ItemCatalog.OakChair)]
    [InlineData(ShopCatalog.ClothingShop, ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds)]
    [InlineData(ShopCatalog.FurnitureShop, ShopCatalog.ClothingShop, ItemCatalog.HatCap)]
    public async Task Buy_FromAShopWhoseCounterTheFarmerIsNotAt_IsRefused(int standAt, int buyFrom, int itemId)
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, standAt);

        (await _shop.BuyAsync(_connection, player, buyFrom, itemId, 1)).Should().Be(new ShopOutcome(ShopStatus.NotAtShop, 0));

        (await _transfers.GetBalanceAsync(player)).Should().Be(500);
    }

    [Fact]
    public async Task DevGrant_GivesItemsAndCoinsInOneTransfer_AndRefusesBadRequests()
    {
        Guid player = await CreatePlayerAsync();
        DevGrantService grants = new(_transfers);

        (await grants.GrantAsync(player, ItemCatalog.OakChair, 5, 250)).Should().BeTrue();
        (await grants.GrantAsync(player, 4242, 1, 0)).Should().BeFalse();
        (await grants.GrantAsync(player, ItemCatalog.OakChair, 0, 0)).Should().BeFalse();
        (await grants.GrantAsync(player, 0, 0, 0)).Should().BeFalse();
        (await grants.GrantAsync(player, 0, 0, -5)).Should().BeFalse();

        (await CountAsync(player, ItemCatalog.OakChair)).Should().Be(5);
        (await _transfers.GetBalanceAsync(player)).Should().Be(750);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == "dev.grant").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(250);
    }

    private void StandAtCounter(Guid player, int shopId)
    {
        ShopPoint point = TownSpec().Shops.Single(p => p.ShopId == shopId);
        _presence.Stand(player, GameMaps.Town, (point.TileX * 16) + 8, (point.TileY * 16) + 8);
    }

    private static MapSpec TownSpec()
    {
        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        return town!;
    }

    private async Task<int> CountAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

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
    private class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    private sealed class TownCatalog : IMapCatalog
    {
        public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
        {
            // Only the tile size matters to the shop; every tile of the blank grid is walkable.
            map = new MapDefinition(mapId, new WalkGrid(130, 110, 16, new bool[130 * 110]), 0, 0);
            return true;
        }
    }

    // Reports one player at a chosen place; everything else is empty.
    private sealed class StandingPresence : IMapPresence
    {
        private PlayerPresence? _player;

        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => _player is null ? 0 : 1;

        [SuppressMessage("Reliability", "CA2000", Justification = "A stub connection that holds nothing to dispose.")]
        public void Stand(Guid account, string mapId, double x, double y)
            => _player = new PlayerPresence(1, account, new MapInstance(mapId, Guid.Empty), DispatchProxy.Create<IConnection, ConnectionStub>(), new MotionState(x, y, 0));

        public void Remove() => _player = null;

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
        {
            player = _player;
            return player is not null;
        }

        public void CopyActiveMapIds(List<string> into)
        {
        }

        public void CopyPlayers(string mapId, List<PlayerPresence> into)
        {
        }

        public ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask MulticastAsync(string mapId, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
