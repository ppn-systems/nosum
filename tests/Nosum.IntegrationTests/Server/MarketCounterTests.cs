// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Market;
using Nosum.Contracts.Market;
using Nosum.Domain.Accounts;
using Nosum.Domain.Market;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Listing, buying and cancelling need the trader's counter in Town; away from it they are refused and nothing moves (docs/adr/ADR-034).</summary>
public sealed class MarketCounterTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly MarketService _market;

    public MarketCounterTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _market = new MarketService(_transfers, _inventory, _units, MarketCounter.Presence(), MarketCounter.Maps(), new CoinSink(), new NicknameDirectory(_units), _clock);
    }

    [Fact]
    public async Task CreateListing_AwayFromTheCounter_IsRefused_AndNothingMoves()
    {
        Guid seller = await NewPlayerAsync();
        int slot = await SlotOfAsync(seller, ItemCatalog.Parsnip);
        int before = await CountAsync(seller, ItemCatalog.Parsnip);

        (await _market.CreateListingAsync(MarketCounter.Away, seller, slot, ItemCatalog.Parsnip, 5, 100)).Should().Be(MarketStatus.NotAtCounter);

        (await CountAsync(seller, ItemCatalog.Parsnip)).Should().Be(before);
        (await _market.MineAsync(seller)).Listings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateListing_AtTheCounter_Works()
    {
        Guid seller = await NewPlayerAsync();
        int before = await CountAsync(seller, ItemCatalog.Parsnip);

        (await _market.CreateListingAsync(MarketCounter.At, seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, 5, 100)).Should().Be(MarketStatus.Ok);

        (await CountAsync(seller, ItemCatalog.Parsnip)).Should().Be(before - 5);
        (await _market.MineAsync(seller)).Listings.Should().ContainSingle();
    }

    [Fact]
    public async Task Buy_AwayFromTheCounter_IsRefused_AndNoCoinsOrItemsMove_AtTheCounterItWorks()
    {
        Guid seller = await NewPlayerAsync();
        Guid buyer = await NewPlayerAsync();
        Guid listing = await ListAsync(seller, 5, 100);
        long coins = await _transfers.GetBalanceAsync(buyer);
        int items = await CountAsync(buyer, ItemCatalog.Parsnip);

        (await _market.BuyAsync(MarketCounter.Away, buyer, listing)).Should().Be(MarketStatus.NotAtCounter);

        (await _transfers.GetBalanceAsync(buyer)).Should().Be(coins);
        (await CountAsync(buyer, ItemCatalog.Parsnip)).Should().Be(items);
        (await StatusAsync(listing)).Should().Be(ListingStatus.Active);

        (await _market.BuyAsync(MarketCounter.At, buyer, listing)).Should().Be(MarketStatus.Ok);
        (await _transfers.GetBalanceAsync(buyer)).Should().Be(coins - 100);
        (await CountAsync(buyer, ItemCatalog.Parsnip)).Should().Be(items + 5);
        (await StatusAsync(listing)).Should().Be(ListingStatus.Sold);
    }

    [Fact]
    public async Task Cancel_AwayFromTheCounter_IsRefused_TheListingStays_AtTheCounterItWorks()
    {
        Guid seller = await NewPlayerAsync();
        Guid listing = await ListAsync(seller, 5, 100);
        int items = await CountAsync(seller, ItemCatalog.Parsnip);

        (await _market.CancelAsync(MarketCounter.Away, seller, listing)).Should().Be(MarketStatus.NotAtCounter);

        (await CountAsync(seller, ItemCatalog.Parsnip)).Should().Be(items);
        (await StatusAsync(listing)).Should().Be(ListingStatus.Active);

        (await _market.CancelAsync(MarketCounter.At, seller, listing)).Should().Be(MarketStatus.Ok);
        (await CountAsync(seller, ItemCatalog.Parsnip)).Should().Be(items + 5);
        (await StatusAsync(listing)).Should().Be(ListingStatus.Cancelled);
    }

    [Fact]
    public async Task ReadingListings_AndTheExpirySweep_NeedNoCounter()
    {
        Guid seller = await NewPlayerAsync();
        Guid listing = await ListAsync(seller, 5, 100);

        (await _market.BrowseAsync(0, [ItemCatalog.Parsnip])).Listings.Should().Contain(l => l.ListingId == listing);
        (await _market.MineAsync(seller)).Listings.Should().ContainSingle();

        _clock.Advance(TimeSpan.FromHours(49));
        (await _market.ExpireDueAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await StatusAsync(listing)).Should().Be(ListingStatus.Expired);
    }

    private async Task<Guid> ListAsync(Guid seller, int quantity, long price)
    {
        (await _market.CreateListingAsync(MarketCounter.At, seller, await SlotOfAsync(seller, ItemCatalog.Parsnip), ItemCatalog.Parsnip, quantity, price)).Should().Be(MarketStatus.Ok);
        return (await _market.MineAsync(seller)).Listings.Single().ListingId;
    }

    private async Task<ListingStatus> StatusAsync(Guid listing)
    {
        await using NosumDbContext context = new(_database.Options);
        return (await context.MarketListings.AsNoTracking().SingleAsync(l => l.Id == listing)).Status;
    }

    private async Task<int> CountAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    private async Task<int> SlotOfAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.First(s => s.ItemId == itemId).Slot;

    private async Task<Guid> NewPlayerAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
        context.Players.Add(player);
        _ = await context.SaveChangesAsync();
        _ = await _inventory.AddAsync(player.Id, ItemCatalog.Parsnip, 20, "test.setup");
        return player.Id;
    }
}
