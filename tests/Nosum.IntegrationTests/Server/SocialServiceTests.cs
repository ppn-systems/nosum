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
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Market;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Economy;
using Nosum.Domain.Market;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Persistence;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The economy services against a real PostgreSQL database: atomic transfers, the market (escrow, fee, races, expiry) and the co-op
/// warehouse. Races run on real parallel tasks; every test checks that nothing was created or lost.
/// </summary>
public sealed class SocialServiceTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly NicknameDirectory _nicknames;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly MarketService _market;
    private readonly CoopService _coops;

    public SocialServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _nicknames = new NicknameDirectory(_units);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _market = new MarketService(_transfers, _inventory, _units, MarketCounter.Presence(), MarketCounter.Maps(), new CoinSink(), _nicknames, _clock);
        _coops = new CoopService(_units, _transfers, new NoPresence(), new NoSessions(), new NoNotifier(), _nicknames, new NoFarm(), new NoEviction(), _clock);
    }

    public void Dispose() => _coops.Dispose();

    [Fact]
    public async Task GetBalance_NewPlayer_GetsTheStartingCoinsOnceAndItIsLogged()
    {
        Guid player = await CreatePlayerAsync();

        (await _transfers.GetBalanceAsync(player)).Should().Be(CoinRules.StartingCoins);
        (await _transfers.GetBalanceAsync(player)).Should().Be(CoinRules.StartingCoins);

        (await CoinLogOfAsync(player)).Should().ContainSingle().Which.Should().Match<CoinLogEntry>(e => e.Reason == "starter" && e.Delta == CoinRules.StartingCoins);
    }

    [Fact]
    public async Task Execute_OneLegCannotBeCovered_NothingMovesAtAll()
    {
        Guid giver = await CreatePlayerAsync();
        Guid taker = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(giver, ItemCatalog.Parsnip, 10, "test.setup");

        // The giver's items are fine, but the taker is asked for more coins than they have.
        TransferStatus status = await _transfers.ExecuteAsync(
            [
                new TransferLeg(TransferParty.Player(giver), ItemCatalog.Parsnip, -10, 0),
                new TransferLeg(TransferParty.Player(taker), ItemCatalog.Parsnip, 10, -(CoinRules.StartingCoins + 1)),
            ],
            "test.transfer");

        status.Should().Be(TransferStatus.NotEnoughCoins);
        (await ParsnipsOfAsync(giver)).Should().Be(10);
        (await ParsnipsOfAsync(taker)).Should().Be(0);
        (await _transfers.GetBalanceAsync(taker)).Should().Be(CoinRules.StartingCoins);
    }

    [Fact]
    public async Task Execute_MoreItemsThanGiverHas_IsRefusedAndTakerGetsNothing()
    {
        Guid giver = await CreatePlayerAsync();
        Guid taker = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(giver, ItemCatalog.Parsnip, 3, "test.setup");

        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(giver), ItemCatalog.Parsnip, -4), new TransferLeg(TransferParty.Player(taker), ItemCatalog.Parsnip, 4)],
            "test.transfer");

        status.Should().Be(TransferStatus.NotEnoughItems);
        (await ParsnipsOfAsync(giver)).Should().Be(3);
        (await ParsnipsOfAsync(taker)).Should().Be(0);
    }

    [Fact]
    public async Task Buy_PaysTheOfflineSellerMinusTheFeeAndMovesTheItems()
    {
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 10, price: 200);

        MarketStatus status = await _market.BuyAsync(buyer, listing);

        status.Should().Be(MarketStatus.Ok);
        (await _transfers.GetBalanceAsync(buyer)).Should().Be(CoinRules.StartingCoins - 200);
        (await _transfers.GetBalanceAsync(seller)).Should().Be(CoinRules.StartingCoins + 200 - 10, "the 5 percent fee of 200 coins is 10");
        (await ParsnipsOfAsync(buyer)).Should().Be(10);
        (await ParsnipsOfAsync(seller)).Should().Be(0);
        (await ListingAsync(listing)).Status.Should().Be(ListingStatus.Sold);
    }

    [Fact]
    public async Task Buy_TheDefaultFeeSinkDestroysTheFee_AndASinkThatReturnsLegsRedirectsIt()
    {
        // Default (owner decision 2026-10-25): the 10 coins of fee exist nowhere afterwards.
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 10, price: 200);
        _ = await _market.BuyAsync(buyer, listing);
        long total = await _transfers.GetBalanceAsync(seller) + await _transfers.GetBalanceAsync(buyer);
        total.Should().Be((2 * CoinRules.StartingCoins) - 10);

        // Another sink pays the fee to a fund; nothing else about the market changes.
        Guid fund = await CreatePlayerAsync();
        MarketService redirecting = new(_transfers, _inventory, _units, MarketCounter.Presence(), MarketCounter.Maps(), new FundSink(fund), _nicknames, _clock);
        (Guid seller2, Guid buyer2, Guid listing2) = await ListedAsync(quantity: 10, price: 200);

        (await redirecting.BuyAsync(buyer2, listing2)).Should().Be(MarketStatus.Ok);

        (await _transfers.GetBalanceAsync(fund)).Should().Be(CoinRules.StartingCoins + 10);
        (await _transfers.GetBalanceAsync(seller2) + await _transfers.GetBalanceAsync(buyer2) + await _transfers.GetBalanceAsync(fund)).Should().Be(3 * CoinRules.StartingCoins);
    }

    private sealed class FundSink(Guid fund) : IFeeSink
    {
        public IReadOnlyList<TransferLeg> Collect(long fee, string reason) => [new TransferLeg(TransferParty.Player(fund), 0, 0, fee)];
    }

    [Fact]
    public async Task Buy_TwoBuyersOneListingInParallel_ExactlyOneSucceeds()
    {
        (Guid seller, _, Guid listing) = await ListedAsync(quantity: 5, price: 100);
        List<Guid> buyers = [];
        for (int i = 0; i < 6; i++)
        {
            buyers.Add(await CreatePlayerAsync());
        }

        MarketStatus[] results = await Task.WhenAll(buyers.Select(b => Task.Run(() => _market.BuyAsync(b, listing))));

        results.Count(r => r == MarketStatus.Ok).Should().Be(1);
        results.Where(r => r != MarketStatus.Ok).Should().OnlyContain(r => r == MarketStatus.ListingGone || r == MarketStatus.Conflict);
        long coins = 0;
        int parsnips = 0;
        foreach (Guid buyer in buyers)
        {
            coins += await _transfers.GetBalanceAsync(buyer);
            parsnips += await ParsnipsOfAsync(buyer);
        }

        parsnips.Should().Be(5, "the stack went to exactly one buyer");
        coins.Should().Be((6 * CoinRules.StartingCoins) - 100, "exactly one buyer paid");
        (await _transfers.GetBalanceAsync(seller)).Should().Be(CoinRules.StartingCoins + 95, "the seller was paid once");
    }

    [Fact]
    public async Task Buy_BuyerCannotAfford_NothingMovesAndTheListingStaysForSale()
    {
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 5, price: CoinRules.StartingCoins + 1);

        (await _market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.NotEnoughCoins);

        (await _transfers.GetBalanceAsync(buyer)).Should().Be(CoinRules.StartingCoins);
        (await _transfers.GetBalanceAsync(seller)).Should().Be(CoinRules.StartingCoins);
        (await ParsnipsOfAsync(buyer)).Should().Be(0);
        (await ListingAsync(listing)).Status.Should().Be(ListingStatus.Active);
    }

    [Fact]
    public async Task Buy_BuyerInventoryFull_NothingMovesAndTheListingStaysForSale()
    {
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 5, price: 50);
        _ = await _inventory.GetAsync(buyer);
        for (int i = 0; i < 40; i++)
        {
            if (!(await _inventory.AddAsync(buyer, ItemCatalog.ShirtRed, 1, "test.fill")).IsOk)
            {
                break;
            }
        }

        (await _market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.NoSpace);

        (await _transfers.GetBalanceAsync(buyer)).Should().Be(CoinRules.StartingCoins);
        (await _transfers.GetBalanceAsync(seller)).Should().Be(CoinRules.StartingCoins);
        (await ListingAsync(listing)).Status.Should().Be(ListingStatus.Active);
    }

    [Fact]
    public async Task Buy_OwnListingExpiredOrMissing_IsRefused()
    {
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 1, price: 10);

        (await _market.BuyAsync(seller, listing)).Should().Be(MarketStatus.OwnListing);
        (await _market.BuyAsync(buyer, Guid.NewGuid())).Should().Be(MarketStatus.ListingGone);
        _clock.Advance(TimeSpan.FromHours(CoinRules.ListingHours + 1));
        (await _market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.Expired);
    }

    [Fact]
    public async Task Cancel_ReturnsTheItemsOnceAndOnlyToTheSeller()
    {
        (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 7, price: 30);

        (await _market.CancelAsync(buyer, listing)).Should().Be(MarketStatus.NotOwner);
        (await _market.CancelAsync(seller, listing)).Should().Be(MarketStatus.Ok);
        (await _market.CancelAsync(seller, listing)).Should().Be(MarketStatus.ListingGone);
        (await _market.BuyAsync(buyer, listing)).Should().Be(MarketStatus.ListingGone);

        (await ParsnipsOfAsync(seller)).Should().Be(7);
        (await ListingAsync(listing)).Status.Should().Be(ListingStatus.Cancelled);
    }

    [Fact]
    public async Task CancelAndBuyInParallel_TheItemsEndUpInExactlyOnePlace()
    {
        for (int round = 0; round < 5; round++)
        {
            (Guid seller, Guid buyer, Guid listing) = await ListedAsync(quantity: 4, price: 20);

            MarketStatus[] results = await Task.WhenAll(Task.Run(() => _market.CancelAsync(seller, listing)), Task.Run(() => _market.BuyAsync(buyer, listing)));

            results.Count(r => r == MarketStatus.Ok).Should().Be(1);
            ((await ParsnipsOfAsync(seller)) + (await ParsnipsOfAsync(buyer))).Should().Be(4);
        }
    }

    [Fact]
    public async Task ExpireDue_ReturnsTheItemsOfOldListingsOnly()
    {
        (Guid seller, _, Guid old) = await ListedAsync(quantity: 3, price: 10);
        _clock.Advance(TimeSpan.FromHours(CoinRules.ListingHours - 1));
        (Guid seller2, _, Guid fresh) = await ListedAsync(quantity: 2, price: 10);
        _clock.Advance(TimeSpan.FromHours(2));

        int returned = await _market.ExpireDueAsync();

        returned.Should().BeGreaterThanOrEqualTo(1, "other tests share the database and may have left listings that are due too");
        (await ListingAsync(old)).Status.Should().Be(ListingStatus.Expired);
        (await ListingAsync(fresh)).Status.Should().Be(ListingStatus.Active);
        (await ParsnipsOfAsync(seller)).Should().Be(3);
        (await ParsnipsOfAsync(seller2)).Should().Be(0);
        (await _market.ExpireDueAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExpireDue_SellerInventoryFull_KeepsTheItemsInEscrowUntilThereIsRoom()
    {
        (Guid seller, _, Guid listing) = await ListedAsync(quantity: 1, price: 10);
        for (int i = 0; i < 40; i++)
        {
            if (!(await _inventory.AddAsync(seller, ItemCatalog.ShirtGreen, 1, "test.fill")).IsOk)
            {
                break;
            }
        }

        _clock.Advance(TimeSpan.FromHours(CoinRules.ListingHours + 1));

        (await _market.ExpireDueAsync()).Should().Be(0);
        (await ListingAsync(listing)).Status.Should().Be(ListingStatus.Active);

        _ = await _inventory.RemoveAsync(seller, ItemCatalog.ShirtGreen, 1, "test.room");
        (await _market.ExpireDueAsync()).Should().Be(1);
        (await ParsnipsOfAsync(seller)).Should().Be(1);
    }

    [Fact]
    public async Task CreateListing_Refusals_LeaveTheInventoryAlone()
    {
        Guid seller = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 5, "test.setup");
        int slot = await SlotOfAsync(seller, ItemCatalog.Parsnip);

        (await _market.CreateListingAsync(seller, slot, ItemCatalog.Parsnip, 5, 0)).Should().Be(MarketStatus.InvalidPrice);
        (await _market.CreateListingAsync(seller, slot, ItemCatalog.Parsnip, 6, 10)).Should().Be(MarketStatus.NotEnough);
        (await _market.CreateListingAsync(seller, slot, ItemCatalog.Radish, 1, 10)).Should().Be(MarketStatus.InvalidSlot);
        (await _market.CreateListingAsync(seller, 0, ItemCatalog.Hoe, 1, 10)).Should().Be(MarketStatus.NotTradable);

        (await ParsnipsOfAsync(seller)).Should().Be(5);
        (await _market.MineAsync(seller)).Listings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateListing_BeyondTheLimit_IsRefused()
    {
        Guid seller = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 99, "test.setup");

        for (int i = 0; i < CoinRules.MaxActiveListings; i++)
        {
            int slot = await SlotOfAsync(seller, ItemCatalog.Parsnip);
            (await _market.CreateListingAsync(seller, slot, ItemCatalog.Parsnip, 1, 10)).Should().Be(MarketStatus.Ok);
        }

        int last = await SlotOfAsync(seller, ItemCatalog.Parsnip);
        (await _market.CreateListingAsync(seller, last, ItemCatalog.Parsnip, 1, 10)).Should().Be(MarketStatus.TooManyListings);
        (await ParsnipsOfAsync(seller)).Should().Be(99 - CoinRules.MaxActiveListings);
    }

    [Fact]
    public async Task Browse_PagesNewestFirstAndFiltersByItem()
    {
        Guid seller = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, 20, "test.setup");
        _ = await _inventory.AddAsync(seller, ItemCatalog.Radish, 20, "test.setup");
        // Other tests share the database: use an item filter no other test lists.
        for (int i = 0; i < 12; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            int slot = await SlotOfAsync(seller, ItemCatalog.Radish);
            (await _market.CreateListingAsync(seller, slot, ItemCatalog.Radish, 1, 10 + i)).Should().Be(MarketStatus.Ok, "listing %d", i);
            if (i == 9)
            {
                _ = await _inventory.AddAsync(seller, ItemCatalog.Radish, 20, "test.more");
            }
        }

        ListingsPageView first = await _market.BrowseAsync(0, [ItemCatalog.Radish]);
        ListingsPageView second = await _market.BrowseAsync(1, [ItemCatalog.Radish]);
        ListingsPageView none = await _market.BrowseAsync(0, [ItemCatalog.Cauliflower, -1]);

        first.Listings.Should().HaveCount(CoinRules.ListingsPerPage);
        first.Listings.Select(l => l.Price).Should().BeInDescendingOrder();
        first.Total.Should().BeGreaterThanOrEqualTo(12);
        second.Listings.Should().NotBeEmpty();
        first.Listings.Should().OnlyContain(l => l.ItemId == ItemCatalog.Radish);
        none.Listings.Should().BeEmpty();
    }

    [Fact]
    public async Task Coop_WarehouseWithdrawals_FounderTakesAnythingMemberOnlyWhatTheyPutIn()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        _ = await _inventory.AddAsync(founder, ItemCatalog.Parsnip, 30, "test.setup");
        _ = await _inventory.AddAsync(member, ItemCatalog.Parsnip, 20, "test.setup");

        (await _coops.DepositAsync(member, ItemCatalog.Parsnip, 10)).Should().Be(CoopStatus.Ok);
        (await _coops.DepositAsync(founder, ItemCatalog.Parsnip, 30)).Should().Be(CoopStatus.Ok);

        (await _coops.WithdrawAsync(member, ItemCatalog.Parsnip, 11)).Should().Be(CoopStatus.NotAllowed, "only 10 are the member's own");
        (await _coops.WithdrawAsync(member, ItemCatalog.Parsnip, 10)).Should().Be(CoopStatus.Ok);
        (await _coops.WithdrawAsync(member, ItemCatalog.Parsnip, 1)).Should().Be(CoopStatus.NotAllowed, "the member's contribution is used up");
        (await _coops.WithdrawAsync(founder, ItemCatalog.Parsnip, 31)).Should().Be(CoopStatus.NotEnough);
        (await _coops.WithdrawAsync(founder, ItemCatalog.Parsnip, 25)).Should().Be(CoopStatus.Ok);

        (await ParsnipsOfAsync(member)).Should().Be(20);
        (await ParsnipsOfAsync(founder)).Should().Be(25);
        (await StockOfAsync(coop, ItemCatalog.Parsnip)).Should().Be(5);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoopLog.Where(e => e.CoopId == coop).ToListAsync()).Select(e => e.Delta).Should().BeEquivalentTo([10, 30, -10, -25]);
    }

    [Fact]
    public async Task Coop_Deposit_ToolsAndMoreThanTheInventoryHolds_AreRefusedAndNothingMoves()
    {
        (Guid founder, _, Guid coop) = await CoopWithMemberAsync();
        _ = await _inventory.AddAsync(founder, ItemCatalog.Parsnip, 3, "test.setup");

        (await _coops.DepositAsync(founder, ItemCatalog.Hoe, 1)).Should().Be(CoopStatus.NotTradable);
        (await _coops.DepositAsync(founder, ItemCatalog.Parsnip, 4)).Should().Be(CoopStatus.NotEnough);

        (await ParsnipsOfAsync(founder)).Should().Be(3);
        (await StockOfAsync(coop, ItemCatalog.Parsnip)).Should().Be(0);
    }

    [Fact]
    public async Task Coop_Leave_FounderHandsOverToTheLongestMemberAndALoneFounderDissolvesAnEmptyCoop()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();

        (await _coops.LeaveAsync(founder)).Should().Be(CoopStatus.Ok);

        (await _coops.GetAsync(member)).Role.Should().Be(CoopRole.Founder);
        (await _coops.GetAsync(founder)).HasCoop.Should().BeFalse();
        (await _coops.KickAsync(member, Guid.NewGuid())).Should().Be(CoopStatus.MemberNotFound);

        _ = await _inventory.AddAsync(member, ItemCatalog.Radish, 2, "test.setup");
        (await _coops.DepositAsync(member, ItemCatalog.Radish, 2)).Should().Be(CoopStatus.Ok);
        (await _coops.LeaveAsync(member)).Should().Be(CoopStatus.CannotLeave);
        (await _coops.WithdrawAsync(member, ItemCatalog.Radish, 2)).Should().Be(CoopStatus.Ok);
        (await _coops.LeaveAsync(member)).Should().Be(CoopStatus.Ok);

        await using NosumDbContext context = new(_database.Options);
        (await context.Coops.AnyAsync(c => c.Id == coop)).Should().BeFalse();
    }

    [Fact]
    public async Task Coop_OnePerPlayerAndNamesAreValidated()
    {
        Guid player = await CreatePlayerAsync();

        (await _coops.CreateAsync(player, "x")).Should().Be(CoopStatus.InvalidName);
        (await _coops.CreateAsync(player, "i18n{\"vi\":\"Vườn\",\"en\":\"Garden\"}")).Should().Be(CoopStatus.Ok);
        (await _coops.CreateAsync(player, "Second co-op")).Should().Be(CoopStatus.AlreadyInCoop);
        (await _coops.GetAsync(player)).Name.Should().Contain("Garden");
    }

    [Fact]
    public async Task Coop_KickAndInvites_FounderOnlyAndInvitesAreConsumed()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        CoopView view = await _coops.GetAsync(founder);
        Guid memberId = view.Members.Single(m => !m.IsMe).MemberId;

        (await _coops.KickAsync(member, view.Members.Single(m => m.IsMe).MemberId)).Should().Be(CoopStatus.NotFounder);
        (await _coops.KickAsync(founder, view.Members.Single(m => m.IsMe).MemberId)).Should().Be(CoopStatus.SelfKick);
        (await _coops.KickAsync(founder, memberId)).Should().Be(CoopStatus.Ok);

        (await _coops.GetAsync(member)).HasCoop.Should().BeFalse();
        (await _coops.RespondAsync(member, Guid.NewGuid(), true)).Should().Be(CoopStatus.InviteNotFound);
        _ = coop;
    }

    private async Task<(Guid Founder, Guid Member, Guid Coop)> CoopWithMemberAsync()
    {
        Guid founder = await CreatePlayerAsync();
        Guid member = await CreatePlayerAsync();
        (await _coops.CreateAsync(founder, "Test co-op")).Should().Be(CoopStatus.Ok);
        Guid coop;
        Guid invite;
        await using (NosumDbContext context = new(_database.Options))
        {
            coop = (await context.CoopMembers.SingleAsync(m => m.PlayerId == founder)).CoopId;
            CoopInvite row = new() { CoopId = coop, PlayerId = member, CreatedAt = s_start, ExpiresAt = s_start.AddDays(3) };
            context.CoopInvites.Add(row);
            _ = await context.SaveChangesAsync();
            invite = row.Id;
        }

        (await _coops.RespondAsync(member, invite, true)).Should().Be(CoopStatus.Ok);
        return (founder, member, coop);
    }

    private async Task<(Guid Seller, Guid Buyer, Guid Listing)> ListedAsync(int quantity, long price)
    {
        Guid seller = await CreatePlayerAsync();
        Guid buyer = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(seller, ItemCatalog.Parsnip, quantity, "test.setup");
        int slot = await SlotOfAsync(seller, ItemCatalog.Parsnip);
        (await _market.CreateListingAsync(seller, slot, ItemCatalog.Parsnip, quantity, price)).Should().Be(MarketStatus.Ok);
        ListingsPageView mine = await _market.MineAsync(seller);
        return (seller, buyer, mine.Listings.Single().ListingId);
    }

    private async Task<int> SlotOfAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.First(s => s.ItemId == itemId).Slot;

    private async Task<int> ParsnipsOfAsync(Guid player)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity);

    private async Task<int> StockOfAsync(Guid coop, int itemId)
    {
        await using NosumDbContext context = new(_database.Options);
        return (await context.CoopStock.Where(s => s.CoopId == coop && s.ItemId == itemId).SingleOrDefaultAsync())?.Quantity ?? 0;
    }

    private async Task<MarketListing> ListingAsync(Guid listing)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.MarketListings.SingleAsync(l => l.Id == listing);
    }

    private async Task<List<CoinLogEntry>> CoinLogOfAsync(Guid player)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.CoinLog.Where(e => e.PlayerId == player).ToListAsync();
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
        context.Players.Add(player);
        _ = await context.SaveChangesAsync();
        return player.Id;
    }

    private sealed class NoEviction : ICoopLandEviction
    {
        public ValueTask EvictAsync(Guid accountId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoFarm : IFarmService
    {
        public Task<FarmUseOutcome> UseAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FarmSnapshotView> GetFarmAsync(IConnection connection, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Forget(MapInstance instance)
        {
        }
    }

    private sealed class NoNotifier : IPlayerNotifier
    {
        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class NoSessions : ISessionRegistry
    {
        public ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Release(IConnection connection)
        {
        }

        public IConnection? GetActive(Guid playerId) => null;
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
