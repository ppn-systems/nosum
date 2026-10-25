// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Market;
using Nosum.Domain.Accounts;
using Nosum.Domain.Market;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The market's anti-cornering limit and its price history against a real PostgreSQL database (docs/adr/ADR-030 D4).</summary>
public sealed class MarketLimitsTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly GameCalendar _calendar = new(s_start.ToUnixTimeMilliseconds() - (GameCalendar.DefaultDayLengthMs / 2), GameCalendar.DefaultDayLengthMs);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly MarketService _market;

    public MarketLimitsTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _market = new MarketService(_transfers, _inventory, _units, new NoPresence(), new NoMaps(), new CoinSink(), new NicknameDirectory(_units), _clock, _calendar);
    }

    // The tests share one database and read whole histories: every test starts without sales.
    public async Task InitializeAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.MarketSales.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateListing_UpToTheLimitOfOneItemInAll_IsAllowed_TheNextUnitIsRefusedAndNothingMoves()
    {
        Guid seller = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 600, "test.setup");

        // 5 x 99 + 5 = 500 units over six listings.
        for (int i = 0; i < 5; i++)
        {
            (await _market.CreateListingAsync(seller, await FullStackSlotAsync(seller), ItemCatalog.Parsnip, 99, 100)).Should().Be(MarketStatus.Ok);
        }

        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 5, 100)).Should().Be(MarketStatus.Ok);
        (await ListedUnitsAsync(seller, ItemCatalog.Parsnip)).Should().Be(CoinRules.MaxListedUnitsPerItem);

        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 1, 100)).Should().Be(MarketStatus.TooManyOfItem);
        (await ListedUnitsAsync(seller, ItemCatalog.Parsnip)).Should().Be(500);
        (await ParsnipsAsync(seller)).Should().Be(100, "the refused unit stayed in the inventory");
    }

    [Fact]
    public async Task CreateListing_CancellingFreesTheRoom_AndAnotherItemOrAnotherPlayerIsNotAffected()
    {
        Guid seller = await NewPlayerAsync();
        Guid other = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 520, "test.setup");
        _ = await _inventory.AddAsync(seller, ItemCatalog.Radish, 40, "test.setup");
        _ = await _inventory.AddAsync(other, ItemCatalog.Parsnip, 99, "test.setup");
        for (int i = 0; i < 5; i++)
        {
            (await _market.CreateListingAsync(seller, await FullStackSlotAsync(seller), ItemCatalog.Parsnip, 99, 100)).Should().Be(MarketStatus.Ok);
        }

        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 6, 100)).Should().Be(MarketStatus.TooManyOfItem, "495 + 6 > 500");
        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Radish), ItemCatalog.Radish, 40, 100)).Should().Be(MarketStatus.Ok, "the limit is per item id");
        (await _market.CreateListingAsync(other, await SlotOfAsync(other, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 99, 100)).Should().Be(MarketStatus.Ok, "the limit is per player");

        Guid cancelled = (await _market.MineAsync(seller)).Listings.First(l => l.ItemId == ItemCatalog.Parsnip).ListingId;
        (await _market.CancelAsync(seller, cancelled)).Should().Be(MarketStatus.Ok);
        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 6, 100)).Should().Be(MarketStatus.Ok, "495 - 99 + 6");
    }

    [Fact]
    public async Task CreateListing_SoldAndExpiredListingsStopCounting()
    {
        Guid seller = await NewPlayerAsync();
        Guid buyer = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 600, "test.setup");
        for (int i = 0; i < 5; i++)
        {
            (await _market.CreateListingAsync(seller, await FullStackSlotAsync(seller), ItemCatalog.Parsnip, 99, 10)).Should().Be(MarketStatus.Ok);
        }

        Guid sold = (await _market.MineAsync(seller)).Listings[0].ListingId;
        (await _market.BuyAsync(buyer, sold)).Should().Be(MarketStatus.Ok);
        (await ListedUnitsAsync(seller, ItemCatalog.Parsnip)).Should().Be(396);

        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 99, 10)).Should().Be(MarketStatus.Ok, "a sold listing is no longer on the market");
    }

    [Fact]
    public async Task CreateListing_ManyAtOnceBySameSeller_NeverListMoreThanTheLimit()
    {
        Guid seller = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 891, "test.setup"); // nine full stacks
        int[] slots = [.. (await _inventory.GetAsync(seller)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Select(s => s.Slot)];

        MarketStatus[] results = await Task.WhenAll(slots.Select(slot => _market.CreateListingAsync(seller, slot, ItemCatalog.Parsnip, 99, 100)));

        results.Count(r => r == MarketStatus.Ok).Should().Be(5, "five stacks of 99 fit under 500, the other four do not");
        results.Where(r => r != MarketStatus.Ok).Should().OnlyContain(r => r == MarketStatus.TooManyOfItem || r == MarketStatus.Conflict);
        (await ListedUnitsAsync(seller, ItemCatalog.Parsnip)).Should().Be(495);
        (await ParsnipsAsync(seller)).Should().Be(891 - 495);
    }

    [Fact]
    public async Task GetPriceHistory_NoSales_IsEmpty_ButKnowsToday()
    {
        PriceHistoryView history = await _market.GetPriceHistoryAsync(ItemCatalog.Cauliflower);

        history.Days.Should().BeEmpty();
        history.Today.Should().Be(_calendar.DayNumberAt(_clock.GetUtcNow().ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task GetPriceHistory_AfterSales_GivesMinMedianMaxOfTheUnitPricesPerGameDay()
    {
        Guid seller = await NewPlayerAsync();
        Guid buyer = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Radish, 90, "test.setup");

        // Day 1: unit prices 10, 12 and 8 (150 / 20 rounds to 8). Day 2: one sale at 3.
        await SellAsync(seller, buyer, ItemCatalog.Radish, 10, 100);
        await SellAsync(seller, buyer, ItemCatalog.Radish, 5, 60);
        await SellAsync(seller, buyer, ItemCatalog.Radish, 20, 150);
        int day1 = _calendar.DayNumberAt(_clock.GetUtcNow().ToUnixTimeMilliseconds());
        _clock.Advance(TimeSpan.FromHours(12));
        await SellAsync(seller, buyer, ItemCatalog.Radish, 10, 30);

        PriceHistoryView history = await _market.GetPriceHistoryAsync(ItemCatalog.Radish);

        history.Days.Should().Equal(new PriceDayView(day1, 8, 10, 12, 3), new PriceDayView(day1 + 1, 3, 3, 3, 1));
        history.Today.Should().Be(day1 + 1);
        (await _market.GetPriceHistoryAsync(ItemCatalog.Parsnip)).Days.Should().BeEmpty("another item has its own history");
    }

    [Fact]
    public async Task GetPriceHistory_RefusedBuysAndCancelledListings_LeaveNoSale()
    {
        Guid seller = await NewPlayerAsync();
        Guid poor = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Cauliflower, 20, "test.setup");
        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Cauliflower), ItemCatalog.Cauliflower, 10, 10_000)).Should().Be(MarketStatus.Ok);
        Guid dear = (await _market.MineAsync(seller)).Listings.Single().ListingId;

        (await _market.BuyAsync(poor, dear)).Should().Be(MarketStatus.NotEnoughCoins);
        (await _market.BuyAsync(seller, dear)).Should().Be(MarketStatus.OwnListing);
        (await _market.CancelAsync(seller, dear)).Should().Be(MarketStatus.Ok);
        (await _market.BuyAsync(poor, dear)).Should().Be(MarketStatus.ListingGone);

        (await _market.GetPriceHistoryAsync(ItemCatalog.Cauliflower)).Days.Should().BeEmpty();
        await using NosumDbContext context = new(_database.Options);
        (await context.MarketSales.CountAsync(s => s.ItemId == ItemCatalog.Cauliflower)).Should().Be(0);
    }

    [Fact]
    public async Task GetPriceHistory_ShowsOnlyTheLastFourteenGameDays_AndOnlyDaysWithSales()
    {
        Guid seller = await NewPlayerAsync();
        Guid buyer = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 30, "test.setup");
        await SellAsync(seller, buyer, ItemCatalog.Parsnip, 1, 20);
        int first = _calendar.DayNumberAt(_clock.GetUtcNow().ToUnixTimeMilliseconds());

        _clock.Advance(TimeSpan.FromHours(12 * 5));
        await SellAsync(seller, buyer, ItemCatalog.Parsnip, 1, 25);
        _clock.Advance(TimeSpan.FromHours(12 * (PriceHistoryRules.HistoryDays - 5)));

        // The first sale is exactly HistoryDays old now and falls out; the day with a sale 14 - 5 days ago stays.
        PriceHistoryView history = await _market.GetPriceHistoryAsync(ItemCatalog.Parsnip);
        history.Days.Select(d => d.Day).Should().Equal(first + 5);
        history.Days.Single().Median.Should().Be(25);
    }

    [Fact]
    public async Task BuyTheSameListingFromManyBuyers_RecordsExactlyOneSale()
    {
        Guid seller = await NewPlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Radish, 10, "test.setup");
        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, ItemCatalog.Radish), ItemCatalog.Radish, 10, 50)).Should().Be(MarketStatus.Ok);
        Guid listing = (await _market.MineAsync(seller)).Listings.Single().ListingId;
        Guid[] buyers = [await NewPlayerAsync(), await NewPlayerAsync(), await NewPlayerAsync(), await NewPlayerAsync()];
        MarketStatus[] results = await Task.WhenAll(buyers.Select(b => _market.BuyAsync(b, listing)));

        results.Count(r => r == MarketStatus.Ok).Should().Be(1);
        await using NosumDbContext context = new(_database.Options);
        (await context.MarketSales.Where(s => s.ItemId == ItemCatalog.Radish).ToListAsync()).Should().ContainSingle().Which.Should().Match<MarketSale>(s => s.Price == 50 && s.Quantity == 10 && s.UnitPrice == 5);
    }

    // -- helpers --

    private async Task SellAsync(Guid seller, Guid buyer, int itemId, int quantity, long price)
    {
        (await _market.CreateListingAsync(seller, await SlotOfAsync(seller, itemId), itemId, quantity, price)).Should().Be(MarketStatus.Ok);
        Guid listing = (await _market.MineAsync(seller)).Listings.Single(l => l.ItemId == itemId && l.Quantity == quantity && l.Price == price).ListingId;
        (await _market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.Ok);
    }

    private async Task<int> ListedUnitsAsync(Guid seller, int itemId)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.MarketListings.Where(l => l.SellerId == seller && l.Status == ListingStatus.Active && l.ItemId == itemId).SumAsync(l => l.Quantity);
    }

    private async Task<int> SlotOfAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.First(s => s.ItemId == itemId).Slot;

    private async Task<int> FullStackSlotAsync(Guid player)
        => (await _inventory.GetAsync(player)).Slots.First(s => s.ItemId == ItemCatalog.Parsnip && s.Quantity == 99).Slot;

    private async Task<int> ParsnipsAsync(Guid player)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity);

    private async Task<Guid> NewPlayerAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
        context.Players.Add(player);
        _ = await context.SaveChangesAsync();
        return player.Id;
    }

    private sealed class NoMaps : IMapCatalog
    {
        public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
        {
            map = null;
            return false;
        }
    }

    private sealed class NoPresence : IMapPresence
    {
        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => 0;

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
        {
            player = null;
            return false;
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
