// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
using Nosum.Core;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // A signed-in guest standing on a tile of a map, holding some coins and items. Nothing of the set-up is measured.
    private async Task<BudgetPlayer> FreshAsync(string mapId, double feetX, double feetY, long coins = 0, params (int ItemId, int Quantity)[] items)
        => await this.FreshAsync(mapId, feetX, feetY, registered: false, coins, items);

    // The same for a registered player (guests may not chat, befriend, visit or found a co-op).
    private async Task<BudgetPlayer> FreshAsync(string mapId, double feetX, double feetY, bool registered, long coins, params (int ItemId, int Quantity)[] items)
    {
        BudgetPlayer p;
        if (registered)
        {
            string nick = this.NextNickname();
            string token = await _rig.RegisteredAccountAsync(nick, "budget." + nick + "@gmail.com");
            p = await _rig.ConnectAsync();
            _ = await p.ResumeAsync(token);
        }
        else
        {
            p = await _rig.SignedInGuestAsync();
        }

        await BudgetRig.Service<IPlayerStateService>().SaveAsync(p.AccountId, new PlayerStateRow(mapId, feetX, feetY, Facing.Down), _rig.Timeout);
        _ = await p.JoinAsync(mapId);
        IDevGrantService grants = BudgetRig.Service<IDevGrantService>();
        bool first = true;
        foreach ((int itemId, int quantity) in items)
        {
            _ = await grants.GrantAsync(p.AccountId, itemId, quantity, first ? coins : 0, _rig.Timeout);
            first = false;
        }

        if (items.Length == 0 && coins > 0)
        {
            _ = await grants.GrantAsync(p.AccountId, ItemCatalog.Parsnip, 1, coins, _rig.Timeout);
        }

        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(250), _rig.Timeout);
        return p;
    }

    private static (double X, double Y) TileCentre(int tx, int ty) => ((tx + 0.5) * 16, (ty + 0.5) * 16);

    private async Task<int> SlotOfAsync(BudgetPlayer p, int itemId)
    {
        using GetInventory request = new();
        InventorySnapshot snapshot = await p.RequestAsync<InventorySnapshot>(request);
        return snapshot.Slots.First(s => s.ItemId == itemId).Slot;
    }

    // Shipping, the market, the shops, the community board and trading: everything that moves coins or items between players.
    private async Task EconomyAsync()
    {
        (double binX, double binY) = (1152, 256);
        await using BudgetPlayer seller = await this.FreshAsync(GameMaps.Farm, binX, binY, 100, (ItemCatalog.Parsnip, 20), (ItemCatalog.Radish, 10));
        int parsnips = await this.SlotOfAsync(seller, ItemCatalog.Parsnip);
        int radishes = await this.SlotOfAsync(seller, ItemCatalog.Radish);

        _ = await _rig.ProbeAsync<ShipResult>(seller, "Market", MarketOpCode.ShipItems, "ShipItems", "ship a stack at the bin", () => new ShipItems { Slot = (byte)parsnips, ItemId = ItemCatalog.Parsnip, Quantity = 5 });
        _ = await _rig.ProbeAsync<MarketResult>(seller, "Market", MarketOpCode.CreateListing, "CreateListing", "first listing", () => new CreateListing { Slot = (byte)radishes, ItemId = ItemCatalog.Radish, Quantity = 5, Price = 200 });
        _ = await _rig.ProbeAsync<ListingsPage>(seller, "Market", MarketOpCode.GetMyListings, "GetMyListings", "one listing", () => new GetMyListings());
        ListingsPage page = await _rig.ProbeAsync<ListingsPage>(seller, "Market", MarketOpCode.BrowseListings, "BrowseListings", "one listing on the page", () => new BrowseListings { Page = 0 });
        _ = await _rig.ProbeAsync<MarketResult>(seller, "Market", MarketOpCode.CreateListing, "CreateListing", "second listing", () => new CreateListing { Slot = (byte)radishes, ItemId = ItemCatalog.Radish, Quantity = 1, Price = 50 });
        page = await ReadPageAsync(seller);

        await using BudgetPlayer buyer = await this.FreshAsync(GameMaps.Town, 696, 920, 5_000, (ItemCatalog.Parsnip, 10));
        if (page.Rows.Count >= 1)
        {
            Guid cheap = page.Rows.OrderBy(l => l.Price).First().ListingId;
            Guid other = page.Rows.OrderByDescending(l => l.Price).First().ListingId;
            _ = await _rig.ProbeAsync<MarketResult>(buyer, "Market", MarketOpCode.BuyListing, "BuyListing", "buy one listing", () => new BuyListing { ListingId = cheap });
            _ = await _rig.ProbeAsync<MarketResult>(seller, "Market", MarketOpCode.CancelListing, "CancelListing", "cancel own listing", () => new CancelListing { ListingId = other });
        }

        // The sale left the seller a notice in the mailbox.
        MailList mails = await _rig.ProbeAsync<MailList>(seller, "Mail", MailOpCode.GetMail, "GetMail", "one sale notice", () => new GetMail());
        if (mails.Mails.Count > 0)
        {
            Guid mail = mails.Mails[0].Id;
            _ = await _rig.ProbeAsync<MailResult>(seller, "Mail", MailOpCode.ReadMail, "ReadMail", "mark read", () => new ReadMail { MailId = mail });
            _ = await _rig.ProbeAsync<MailResult>(seller, "Mail", MailOpCode.CollectMail, "CollectMail", "no attachment", () => new CollectMail { MailId = mail });
            _ = await _rig.ProbeAsync<MailResult>(seller, "Mail", MailOpCode.DeleteMail, "DeleteMail", "delete", () => new DeleteMail { MailId = mail });
        }

        // Shops and the community board, both in the Town.
        (double shopX, double shopY) = TileCentre(43, 57);
        await using BudgetPlayer shopper = await this.FreshAsync(GameMaps.Town, shopX, shopY, 5_000, (ItemCatalog.Parsnip, 30));
        _ = await _rig.ProbeAsync<ShopResult>(shopper, "Shop", ShopOpCode.BuyFromShop, "BuyFromShop", "buy seeds", () => new BuyFromShop { ShopId = Nosum.Gameplay.Shops.ShopCatalog.SeedShop, ItemId = ItemCatalog.ParsnipSeeds, Quantity = 2 });

        (double boardX, double boardY) = TileCentre(42, 57);
        await using BudgetPlayer helper = await this.FreshAsync(GameMaps.Town, boardX, boardY, 0, (ItemCatalog.Parsnip, 30));
        _ = await _rig.ProbeAsync<GoalsSnapshot>(helper, "Goals", GoalOpCode.GetGoals, "GetGoals", "at the board", () => new GetGoals());
        _ = await _rig.ProbeAsync<DeliverResult>(helper, "Goals", GoalOpCode.DeliverToGoal, "DeliverToGoal", "deliver parsnips", () => new DeliverToGoal { GoalId = Nosum.Gameplay.Community.CommunityGoalCatalog.SpringBridge, Quantity = 10 });
    }

    private async Task<ListingsPage> ReadPageAsync(BudgetPlayer p)
    {
        using BrowseListings request = new() { Page = 0 };
        return await p.RequestAsync<ListingsPage>(request);
    }
}
