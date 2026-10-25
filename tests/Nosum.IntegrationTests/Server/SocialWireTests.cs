// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Inventory;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Players;
using Nosum.Gameplay.Shops;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Trade;
using Nosum.Domain.Economy;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The social layer over real Nalix WebSocket sessions against the real host and a real PostgreSQL database: looks broadcast to a second
/// client, the market (a buy race, offline payment), the shipping bin, direct trade (swap, cleared confirmations, disconnect, an atomic
/// failure) and the co-op warehouse permissions. Clients are paced because every client shares one address and its rate limits.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class SocialWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    // Tile (71, 15), just below the shipping bin at (71, 14).
    private static readonly (double X, double Y) s_besideTheBin = ((71 * 16) + 8, (15 * 16) + 8);

    private readonly PostgresFixture _database;

    public SocialWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Looks_ChangedByOnePlayer_AreBroadcastToTheOtherAsIdsAndSurviveAReconnect()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        string token;
        {
            await using SocialClient a = await SocialClient.ConnectAsync(server.Port, ct);
            await using SocialClient b = await SocialClient.ConnectAsync(server.Port, ct);
            token = a.SessionToken;
            await a.JoinAsync(ct);
            await b.JoinAsync(ct);

            // A new player (a guest too) has the default look; the second client learns it of the first on joining.
            (await a.GetLooksAsync(ct)).Single(l => l.PlayerId == a.MapPlayerId).Should().BeEquivalentTo(new LookRow { PlayerId = a.MapPlayerId, SkinId = 1, HairStyleId = 1, HairDyeId = 1, Nickname = a.Nickname });
            LookRow[] seenByB = await b.GetLooksAsync(ct);
            seenByB.Select(l => l.PlayerId).Should().BeEquivalentTo([a.MapPlayerId, b.MapPlayerId]);
            (await a.Looks.WaitAsync(l => l.PlayerId == b.MapPlayerId, ct)).SkinId.Should().Be(1, "the joiner's look is announced to the map");

            // The wardrobe: invalid parts are refused, valid ones reach the other client as ids.
            (await a.SetAppearanceAsync(99, 1, 1, ct)).Should().Be(LookStatus.InvalidSkin);
            (await a.SetAppearanceAsync(1, 99, 1, ct)).Should().Be(LookStatus.InvalidHairStyle);
            (await a.SetAppearanceAsync(3, 4, 2, ct)).Should().Be(LookStatus.Ok);
            LookRow changed = await b.Looks.WaitAsync(l => l.PlayerId == a.MapPlayerId && l.SkinId == 3, ct);
            (changed.HairStyleId, changed.HairDyeId).Should().Be(((byte)4, (byte)2));

            // Clothing comes out of the inventory: only owned clothing can be worn, and what was worn goes back. New players own none, so the tester grant supplies it.
            (await a.CountAsync(ItemCatalog.ShirtRed, ct)).Should().Be(0, "new players start without clothes");
            foreach (int clothes in new[] { ItemCatalog.ShirtRed, ItemCatalog.ShirtGreen, ItemCatalog.HatCap })
            {
                (await a.GrantAsync(clothes, 1, 0, ct)).Should().BeTrue();
            }

            int red = await a.SlotOfAsync(ItemCatalog.ShirtRed, ct);
            (await a.EquipAsync(0, ItemCatalog.Hoe, ct)).Should().Be(LookStatus.NotWearable);
            (await a.EquipAsync(30, ItemCatalog.ShirtGreen, ct)).Should().Be(LookStatus.InvalidSlot);
            (await a.EquipAsync(red, ItemCatalog.ShirtRed, ct)).Should().Be(LookStatus.Ok);
            (await b.Looks.WaitAsync(l => l.PlayerId == a.MapPlayerId && l.ShirtItemId == ItemCatalog.ShirtRed, ct)).SkinId.Should().Be(3);
            (await a.CountAsync(ItemCatalog.ShirtRed, ct)).Should().Be(0, "a worn shirt is not in the inventory");

            int green = await a.SlotOfAsync(ItemCatalog.ShirtGreen, ct);
            (await a.EquipAsync(green, ItemCatalog.ShirtGreen, ct)).Should().Be(LookStatus.Ok);
            (await a.CountAsync(ItemCatalog.ShirtRed, ct)).Should().Be(1, "the shirt worn before went back");
            (await a.UnequipAsync((int)WearSlot.Hat, ct)).Should().Be(LookStatus.NothingWorn);
            int hat = await a.SlotOfAsync(ItemCatalog.HatCap, ct);
            (await a.EquipAsync(hat, ItemCatalog.HatCap, ct)).Should().Be(LookStatus.Ok);
            (await a.UnequipAsync((int)WearSlot.Hat, ct)).Should().Be(LookStatus.Ok);
            (await b.Looks.WaitAsync(l => l.PlayerId == a.MapPlayerId && l.HatItemId == 0 && l.ShirtItemId == ItemCatalog.ShirtGreen, ct)).HairDyeId.Should().Be(2);
        }

        // The look is persisted: a new connection of the same player gets it back.
        await using SocialClient again = await SocialClient.ResumeAsync(server.Port, token, ct);
        await again.JoinAsync(ct);
        LookRow mine = (await again.GetLooksAsync(ct)).Single(l => l.PlayerId == again.MapPlayerId);
        (mine.SkinId, mine.HairStyleId, mine.HairDyeId, mine.ShirtItemId).Should().Be(((byte)3, (byte)4, (byte)2, ItemCatalog.ShirtGreen));
    }

    [Fact]
    public async Task Market_TwoBuyersOneListing_ExactlyOneSucceedsAndTheOfflineSellerIsPaid()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient seller = await SocialClient.ConnectAsync(server.Port, ct);
        await using SocialClient buyerB = await SocialClient.ConnectAsync(server.Port, ct);
        await using SocialClient buyerC = await SocialClient.ConnectAsync(server.Port, ct);
        foreach (SocialClient c in new[] { seller, buyerB, buyerC })
        {
            await c.JoinAsync(ct);
        }

        _ = await server.Inventory.AddAsync(seller.PlayerId, ItemCatalog.Parsnip, 10, "test.setup");
        int slot = await seller.SlotOfAsync(ItemCatalog.Parsnip, ct);

        // Listing: the stack leaves the inventory into escrow.
        (await seller.ListAsync(slot, ItemCatalog.Parsnip, 10, 100, ct)).Should().Be(MarketStatus.Ok);
        (await seller.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(0);
        (await seller.ListAsync(slot, ItemCatalog.Parsnip, 10, 100, ct)).Should().Be(MarketStatus.InvalidSlot, "the slot is empty now");
        (int total, ListingRow[] rows) = await buyerB.BrowseAsync([ItemCatalog.Parsnip], ct);
        total.Should().BeGreaterThanOrEqualTo(1);
        ListingRow listing = rows.Should().ContainSingle(r => r.Quantity == 10 && r.Price == 100).Subject;
        listing.IsMine.Should().BeFalse();
        (await seller.BrowseAsync([ItemCatalog.Parsnip], ct)).Rows.Single(r => r.ListingId == listing.ListingId).IsMine.Should().BeTrue();

        // The seller is gone when the sale happens.
        await seller.DisposeAsync();

        MarketStatus[] results = await Task.WhenAll(buyerB.BuyAsync(listing.ListingId, ct), buyerC.BuyAsync(listing.ListingId, ct));

        results.Count(r => r == MarketStatus.Ok).Should().Be(1);
        results.Single(r => r != MarketStatus.Ok).Should().Be(MarketStatus.ListingGone);
        SocialClient winner = results[0] == MarketStatus.Ok ? buyerB : buyerC;
        SocialClient loser = winner == buyerB ? buyerC : buyerB;
        (await winner.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(10);
        (await loser.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(0);
        (await winner.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins - 100);
        (await loser.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins);
        await using NosumDbContext context = new(_database.Options);
        (await context.PlayerWallets.SingleAsync(w => w.PlayerId == seller.PlayerId)).Coins.Should().Be(CoinRules.StartingCoins + 95, "the price minus the 5 percent fee, paid while offline");
    }

    [Fact]
    public async Task Shipping_AtTheBinPaysTheFixedPrice_FarFromItOrWithSeedsIsRefused()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient farmer = await SocialClient.ConnectAsync(server.Port, ct, (Program.FarmMapId, s_besideTheBin.X, s_besideTheBin.Y));
        await using SocialClient wanderer = await SocialClient.ConnectAsync(server.Port, ct);
        await farmer.JoinAsync(ct);
        await wanderer.JoinAsync(ct);
        _ = await server.Inventory.AddAsync(farmer.PlayerId, ItemCatalog.Parsnip, 5, "test.setup");
        _ = await server.Inventory.AddAsync(wanderer.PlayerId, ItemCatalog.Parsnip, 5, "test.setup");

        int slot = await farmer.SlotOfAsync(ItemCatalog.Parsnip, ct);
        (await farmer.ShipAsync(0, ItemCatalog.Hoe, 1, ct)).Status.Should().Be(MarketStatus.NotShippable);
        (await farmer.ShipAsync(slot, ItemCatalog.Radish, 1, ct)).Status.Should().Be(MarketStatus.InvalidSlot);
        (await wanderer.ShipAsync(await wanderer.SlotOfAsync(ItemCatalog.Parsnip, ct), ItemCatalog.Parsnip, 5, ct)).Status.Should().Be(MarketStatus.NotAtBin);

        (MarketStatus status, long earned) = await farmer.ShipAsync(slot, ItemCatalog.Parsnip, 0, ct);

        status.Should().Be(MarketStatus.Ok);
        ShippingPrices.TryGet(ItemCatalog.Parsnip, out int each).Should().BeTrue();
        earned.Should().Be(5L * each);
        (await farmer.Wallet.WaitAsync(c => c == CoinRules.StartingCoins + earned, ct)).Should().Be(CoinRules.StartingCoins + earned);
        (await farmer.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(0);
        (await wanderer.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(5);
    }

    [Fact]
    public async Task Trade_BothConfirm_SwapsItemsAndCoinsAtOnce()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        (SocialClient a, SocialClient b) = await TradersAsync(server);
        await using SocialClient disposeA = a;
        await using SocialClient disposeB = b;

        (await a.TradeOfferAsync([(ItemCatalog.Parsnip, 5)], 50, ct)).Should().Be(TradeStatus.Ok);
        TradeCopy seenByB = await b.TradeStates.WaitAsync(s => s.OtherItems.Length == 1 && s.OtherCoins == 50, ct);
        seenByB.OtherItems.Should().Equal([(ItemCatalog.Parsnip, 5)]);
        (await b.TradeOfferAsync([(ItemCatalog.Radish, 3)], 0, ct)).Should().Be(TradeStatus.Ok);

        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);
        (await b.TradeStates.WaitAsync(s => s.OtherConfirmed, ct)).MyConfirmed.Should().BeFalse();
        (await b.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);

        (await a.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.Completed);
        (await b.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.Completed);
        (await a.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(5);
        (await a.CountAsync(ItemCatalog.Radish, ct)).Should().Be(3);
        (await b.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(5);
        (await b.CountAsync(ItemCatalog.Radish, ct)).Should().Be(7);
        (await a.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins - 50);
        (await b.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins + 50);
        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.NoSession);
    }

    [Fact]
    public async Task Trade_ChangingAnOfferClearsBothConfirmations_AndOffersBeyondWhatIsOwnedAreRefused()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        (SocialClient a, SocialClient b) = await TradersAsync(server);
        await using SocialClient disposeA = a;
        await using SocialClient disposeB = b;

        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.NothingOffered);
        (await a.TradeOfferAsync([(ItemCatalog.Parsnip, 11)], 0, ct)).Should().Be(TradeStatus.NotEnoughItems);
        (await a.TradeOfferAsync([], CoinRules.StartingCoins + 1, ct)).Should().Be(TradeStatus.NotEnoughCoins);
        (await a.TradeOfferAsync([(ItemCatalog.Hoe, 1)], 0, ct)).Should().Be(TradeStatus.InvalidOffer);
        (await a.TradeOfferAsync([(ItemCatalog.Parsnip, 2)], 0, ct)).Should().Be(TradeStatus.Ok);
        (await b.TradeOfferAsync([(ItemCatalog.Radish, 2)], 0, ct)).Should().Be(TradeStatus.Ok);
        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);
        (await b.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);
        // The second confirmation completed the trade; start a fresh one to test clearing.
        (await a.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.Completed);
        (await b.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.Completed);

        (await a.TradeRequestAsync(b.MapPlayerId, ct)).Should().Be(TradeStatus.Ok);
        _ = await b.Invites.WaitAsync(_ => true, ct);
        (await b.TradeRespondAsync(true, ct)).Should().Be(TradeStatus.Ok);
        (await a.TradeOfferAsync([(ItemCatalog.Parsnip, 1)], 0, ct)).Should().Be(TradeStatus.Ok);
        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);
        _ = await b.TradeStates.WaitAsync(s => s.OtherConfirmed, ct);

        // B changes the offer: A's confirmation is gone on both screens.
        (await b.TradeOfferAsync([(ItemCatalog.Radish, 1)], 0, ct)).Should().Be(TradeStatus.Ok);

        TradeCopy cleared = await b.TradeStates.WaitAsync(s => s.MyItems.Length == 1 && !s.OtherConfirmed && !s.MyConfirmed, ct);
        cleared.OtherItems.Should().Equal([(ItemCatalog.Parsnip, 1)]);
        TradeCopy clearedForA = await a.TradeStates.WaitAsync(s => s.OtherItems.Length == 1 && !s.MyConfirmed, ct);
        clearedForA.OtherConfirmed.Should().BeFalse();
        (await a.TradeCancelAsync(ct)).Should().Be(TradeStatus.Ok);
        (await b.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.CancelledByOther);
        (await a.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(8, "only the first trade moved anything");
    }

    [Fact]
    public async Task Trade_OtherPlayerDisconnects_TheTradeEndsAndNothingMoves()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        (SocialClient a, SocialClient b) = await TradersAsync(server);
        await using SocialClient disposeA = a;
        (await a.TradeOfferAsync([(ItemCatalog.Parsnip, 4)], 10, ct)).Should().Be(TradeStatus.Ok);
        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);

        await b.DisposeAsync();

        (await a.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.OtherLeft);
        (await a.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(10);
        (await a.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins);
        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.NoSession);
    }

    [Fact]
    public async Task Trade_ASideNoLongerHasItsItemsAtTheSwap_NothingMovesForAnyone()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        (SocialClient a, SocialClient b) = await TradersAsync(server);
        await using SocialClient disposeA = a;
        await using SocialClient disposeB = b;
        (await a.TradeOfferAsync([(ItemCatalog.Parsnip, 10)], 0, ct)).Should().Be(TradeStatus.Ok);
        (await b.TradeOfferAsync([(ItemCatalog.Radish, 1)], 100, ct)).Should().Be(TradeStatus.Ok);
        (await a.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);

        // After offering, A puts the whole stack on the market: the claim is stale by the time B confirms.
        int slot = await a.SlotOfAsync(ItemCatalog.Parsnip, ct);
        (await a.ListAsync(slot, ItemCatalog.Parsnip, 10, 50, ct)).Should().Be(MarketStatus.Ok);
        (await b.TradeConfirmAsync(ct)).Should().Be(TradeStatus.Ok);

        (await b.TradeEnds.WaitAsync(_ => true, ct)).Should().Be(TradeEndReason.NotEnoughItems);
        (await b.CountAsync(ItemCatalog.Radish, ct)).Should().Be(10);
        (await b.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(0);
        (await b.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins, "B's coins did not move either");
        (await a.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins);
    }

    [Fact]
    public async Task Coop_FoundInviteDepositWithdraw_RolesDecideWhoMayTakeWhat()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient founder = await SocialClient.ConnectAsync(server.Port, ct);
        await using SocialClient member = await SocialClient.ConnectAsync(server.Port, ct);
        await founder.JoinAsync(ct);
        await member.JoinAsync(ct);
        _ = await server.Inventory.AddAsync(founder.PlayerId, ItemCatalog.Parsnip, 20, "test.setup");
        _ = await server.Inventory.AddAsync(member.PlayerId, ItemCatalog.Parsnip, 20, "test.setup");

        (await founder.CreateCoopAsync("x", ct)).Should().Be(CoopStatus.InvalidName);
        (await founder.CreateCoopAsync("i18n{\"vi\":\"Vườn Xanh\",\"en\":\"Green Garden\"}", ct)).Should().Be(CoopStatus.Ok);
        (await founder.CreateCoopAsync("Another", ct)).Should().Be(CoopStatus.AlreadyInCoop);
        (await member.CoopInviteAsync(founder.MapPlayerId, ct)).Should().Be(CoopStatus.NotInCoop);

        // The invitation reaches the invited player as a pushed snapshot; accepting puts both in the same co-op.
        (await founder.CoopInviteAsync(member.MapPlayerId, ct)).Should().Be(CoopStatus.Ok);
        CoopCopy invited = await member.Coops.WaitAsync(c => !c.HasCoop && c.InviteIds.Length == 1, ct);
        (await founder.CoopInviteAsync(member.MapPlayerId, ct)).Should().Be(CoopStatus.AlreadyInvited);
        (await member.CoopRespondAsync(invited.InviteIds[0], true, ct)).Should().Be(CoopStatus.Ok);
        CoopCopy joined = await founder.Coops.WaitAsync(c => c.Members.Length == 2, ct);
        joined.Role.Should().Be(CoopRole.Founder);
        (await member.Coops.WaitAsync(c => c.HasCoop && c.Role == CoopRole.Member, ct)).Name.Should().Contain("Green Garden");
        (await member.CoopInviteAsync(founder.MapPlayerId, ct)).Should().Be(CoopStatus.NotFounder);

        // The warehouse: deposits are audited, a member may only take back what they put in, the founder takes anything.
        (await member.DepositAsync(ItemCatalog.Parsnip, 10, ct)).Should().Be(CoopStatus.Ok);
        (await founder.DepositAsync(ItemCatalog.Parsnip, 20, ct)).Should().Be(CoopStatus.Ok);
        (await member.DepositAsync(ItemCatalog.Hoe, 1, ct)).Should().Be(CoopStatus.NotTradable);
        CoopCopy stock = await member.Coops.WaitAsync(c => c.Stock.Any(s => s.ItemId == ItemCatalog.Parsnip && s.Quantity == 30), ct);
        stock.Stock.Single().Available.Should().Be(10, "a member may take back what they put in");
        (await member.WithdrawAsync(ItemCatalog.Parsnip, 11, ct)).Should().Be(CoopStatus.NotAllowed);
        (await member.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(10);
        (await member.WithdrawAsync(ItemCatalog.Parsnip, 10, ct)).Should().Be(CoopStatus.Ok);
        (await member.WithdrawAsync(ItemCatalog.Parsnip, 1, ct)).Should().Be(CoopStatus.NotAllowed);
        (await founder.WithdrawAsync(ItemCatalog.Parsnip, 20, ct)).Should().Be(CoopStatus.Ok);
        (await founder.WithdrawAsync(ItemCatalog.Parsnip, 1, ct)).Should().Be(CoopStatus.NotEnough);
        (await founder.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(20);

        // Only the founder removes members; the removed member is told at once.
        Guid memberId = joined.Members.Single(m => !m.IsMe).MemberId;
        (await member.CoopKickAsync(joined.Members.Single(m => m.IsMe).MemberId, ct)).Should().Be(CoopStatus.NotFounder);
        (await founder.CoopKickAsync(memberId, ct)).Should().Be(CoopStatus.Ok);
        (await member.Coops.WaitAsync(c => !c.HasCoop, ct)).Members.Should().BeEmpty();
        (await member.WithdrawAsync(ItemCatalog.Parsnip, 1, ct)).Should().Be(CoopStatus.NotInCoop);
    }

    // Two players on the farm, A holding 10 parsnips and B 10 radishes, with a trade open between them.
    private async Task<(SocialClient A, SocialClient B)> TradersAsync(Server server)
    {
        CancellationToken ct = server.Token;
        SocialClient a = await SocialClient.ConnectAsync(server.Port, ct);
        SocialClient b = await SocialClient.ConnectAsync(server.Port, ct);
        await a.JoinAsync(ct);
        await b.JoinAsync(ct);
        _ = await server.Inventory.AddAsync(a.PlayerId, ItemCatalog.Parsnip, 10, "test.setup");
        _ = await server.Inventory.AddAsync(b.PlayerId, ItemCatalog.Radish, 10, "test.setup");

        (await a.TradeRequestAsync(a.MapPlayerId, ct)).Should().Be(TradeStatus.SelfTrade);
        (await a.TradeRequestAsync(4242, ct)).Should().Be(TradeStatus.TargetNotFound);
        (await a.TradeRequestAsync(b.MapPlayerId, ct)).Should().Be(TradeStatus.Ok);
        (await b.Invites.WaitAsync(id => id == a.MapPlayerId, ct)).Should().Be(a.MapPlayerId);
        (await b.TradeRespondAsync(true, ct)).Should().Be(TradeStatus.Ok);
        _ = await a.TradeStates.WaitAsync(_ => true, ct);
        _ = await b.TradeStates.WaitAsync(_ => true, ct);
        return (a, b);
    }

    [Fact]
    public async Task Shop_BuyOverTheWire_NeedsTheCounter_TakesCoinsAndGivesTheItem()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient far = await SocialClient.ConnectAsync(server.Port, ct);
        await far.JoinAsync(ct);
        (await far.BuyFromShopAsync(ShopCatalog.GeneralStore, ItemCatalog.OakChair, 1, ct)).Status.Should().Be(ShopStatus.NotAtShop, "the bus stop has no shop");

        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        ShopPoint counter = town!.Shops[0];
        await using SocialClient buyer = await SocialClient.ConnectAsync(server.Port, ct, (GameMaps.Town, (counter.TileX * 16) + 8, (counter.TileY * 16) + 8));
        await buyer.JoinAsync(ct);

        (await buyer.BuyFromShopAsync(ShopCatalog.GeneralStore, ItemCatalog.Hoe, 1, ct)).Status.Should().Be(ShopStatus.NotSold);
        (await buyer.BuyFromShopAsync(ShopCatalog.GeneralStore, ItemCatalog.OakChair, 0, ct)).Status.Should().Be(ShopStatus.InvalidQuantity);
        (await buyer.BuyFromShopAsync(ShopCatalog.GeneralStore, ItemCatalog.BlueCouch, 2, ct)).Status.Should().Be(ShopStatus.NotEnoughCoins);
        (await buyer.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins);

        (await buyer.BuyFromShopAsync(ShopCatalog.GeneralStore, ItemCatalog.OakChair, 2, ct)).Should().Be((ShopStatus.Ok, 200));
        (await buyer.Wallet.WaitAsync(c => c == 300, ct)).Should().Be(300);
        (await buyer.Slots.WaitAsync(s => s.ItemId == ItemCatalog.OakChair && s.Quantity == 2, ct)).Quantity.Should().Be(2);
        (await buyer.CountAsync(ItemCatalog.OakChair, ct)).Should().Be(2);
        (await buyer.CountAsync(ItemCatalog.ShirtRed, ct)).Should().Be(0, "no clothes in the starter kit");
    }

    [Fact]
    public async Task Nicknames_AreSentWithTheLook_SeenByTheOtherClient_AndRenamesArePushedAndRateLimitedByTheDay()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient a = await SocialClient.ConnectAsync(server.Port, ct);
        await using SocialClient b = await SocialClient.ConnectAsync(server.Port, ct);
        await a.JoinAsync(ct);
        await b.JoinAsync(ct);
        NicknameRules.IsGuestName(a.Nickname, out _).Should().BeTrue("a guest has a neutral name the client localizes");

        LookRow[] seenByB = await b.GetLooksAsync(ct);
        seenByB.Single(l => l.PlayerId == a.MapPlayerId).Nickname.Should().Be(a.Nickname);
        seenByB.Single(l => l.PlayerId == b.MapPlayerId).Nickname.Should().Be(b.Nickname);

        string chosen = "Nguyễn Văn_An";
        NicknameResult renamed = await a.SetNicknameAsync(chosen, ct);
        renamed.Status.Should().Be(AccountStatus.Ok);
        renamed.Nickname.Should().Be(chosen);
        (await b.Looks.WaitAsync(l => l.PlayerId == a.MapPlayerId && l.Nickname == chosen, ct)).Nickname.Should().Be(chosen);

        NicknameResult again = await a.SetNicknameAsync("Another_Name", ct);
        again.Status.Should().Be(AccountStatus.NicknameTooSoon);
        again.NextChangeAtMs.Should().BeGreaterThan(0);
        (await b.SetNicknameAsync("nguyễn văn an", ct)).Status.Should().Be(AccountStatus.NicknameTaken, "case-insensitive, underscore reads as a space");
        (await b.SetNicknameAsync("Admin", ct)).Status.Should().Be(AccountStatus.NicknameReserved);
        (await b.SetNicknameAsync("x", ct)).Status.Should().Be(AccountStatus.InvalidNickname);
    }

    [Fact]
    public async Task Market_ListingsCarryTheSellerNickname_AndTheFeeIsDestroyed()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient seller = await SocialClient.ConnectAsync(server.Port, ct);
        await using SocialClient buyer = await SocialClient.ConnectAsync(server.Port, ct);
        await seller.JoinAsync(ct);
        await buyer.JoinAsync(ct);
        (await seller.GrantAsync(ItemCatalog.OakChair, 1, 0, ct)).Should().BeTrue();
        int slot = await seller.SlotOfAsync(ItemCatalog.OakChair, ct);
        (await seller.ListAsync(slot, ItemCatalog.OakChair, 1, 400, ct)).Should().Be(MarketStatus.Ok);

        (int total, ListingRow[] rows) = await buyer.BrowseAsync([ItemCatalog.OakChair], ct);

        total.Should().BeGreaterThanOrEqualTo(1);
        rows.Should().Contain(r => r.SellerNickname == seller.Nickname && !r.IsMine);
        (await buyer.BuyAsync(rows.First(r => r.SellerNickname == seller.Nickname).ListingId, ct)).Should().Be(MarketStatus.Ok);
        (await buyer.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins - 400);
        (await seller.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins + 380, "the 5 % fee (20 coins) left the economy");
    }

    [Fact]
    public async Task DevGrant_InDevelopment_Works_AndInProductionTheOpcodeIsNotHandledAtAll()
    {
        await using (Server development = await Server.StartAsync(_database, development: true))
        {
            await using SocialClient a = await SocialClient.ConnectAsync(development.Port, development.Token);
            await a.JoinAsync(development.Token);
            (await a.GrantAsync(ItemCatalog.OakChair, 3, 700, development.Token)).Should().BeTrue();
            (await a.CountAsync(ItemCatalog.OakChair, development.Token)).Should().Be(3);
            (await a.CoinsAsync(development.Token)).Should().Be(CoinRules.StartingCoins + 700);
        }

        await using Server production = await Server.StartAsync(_database, development: false);
        await using SocialClient b = await SocialClient.ConnectAsync(production.Port, production.Token);
        await b.JoinAsync(production.Token);
        (await b.GrantIsAnsweredAsync(production.Token)).Should().BeFalse("Production never maps the tester handler");
        (await b.CountAsync(ItemCatalog.OakChair, production.Token)).Should().Be(0);
        (await b.CoinsAsync(production.Token)).Should().Be(CoinRules.StartingCoins);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>The real host on a free port, with a token that expires so a stuck test fails instead of hanging.</summary>
    private sealed class Server : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private Server(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Token => _timeout.Token;

        public IInventoryService Inventory => InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The server is returned to the test, which disposes it.")]
        public static async Task<Server> StartAsync(PostgresFixture database, bool development = true)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            NetworkApplication app = Program.BuildApplication(port, new FakeTimeProvider(s_start), development);
            Server server = new(app, port);
            await app.ActivateAsync(server.Token);
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
