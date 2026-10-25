// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.C2S;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Node;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.C2S;
using Nosum.Contracts.Quest.S2C;
using Nosum.Contracts.Season;
using Nosum.Contracts.Season.C2S;
using Nosum.Contracts.Season.S2C;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Host;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // A player standing on the farm asks for every snapshot the client asks for when it opens a panel or a screen.
    private async Task WorldAsync()
    {
        await using BudgetPlayer p = await _rig.SignedInGuestAsync();
        _ = await p.JoinAsync(Program.FarmMapId);
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);

        _ = await _rig.ProbeAsync<InventorySnapshot>(p, "Inventory", InventoryOpCode.GetInventory, "GetInventory", "starter kit", () => new GetInventory());
        _ = await _rig.ProbeAsync<InventoryResult>(p, "Inventory", InventoryOpCode.SelectSlot, "SelectSlot", "hotbar slot 1", () => new SelectSlot { Slot = 1 });
        _ = await _rig.ProbeAsync<InventoryResult>(p, "Inventory", InventoryOpCode.MoveSlot, "MoveSlot", "split a stack into a free slot", () => new MoveSlot { From = 2, To = 20, Quantity = 1 });
        _ = await _rig.ProbeAsync<FarmSnapshot>(p, "Farm", FarmOpCode.GetFarm, "GetFarm", "empty farm", () => new GetFarm());
        _ = await _rig.ProbeAsync<NodeSnapshot>(p, "Node", NodeOpCode.GetNodes, "GetNodes", "farm", () => new GetNodes());
        _ = await _rig.ProbeAsync<GroundItemsSnapshot>(p, "GroundItem", GroundItemOpCode.GetGroundItems, "GetGroundItems", "nothing on the ground", () => new GetGroundItems());
        _ = await _rig.ProbeAsync<QuestLog>(p, "Quest", QuestOpCode.GetQuests, "GetQuests", "new player", () => new GetQuests());
        _ = await _rig.ProbeAsync<CalendarInfo>(p, "Calendar", SeasonOpCode.GetCalendar, "GetCalendar", "calendar", () => new GetCalendar());
        _ = await _rig.ProbeAsync<PlacedSnapshot>(p, "Placeable", PlaceableOpCode.GetPlaced, "GetPlaced", "empty farm", () => new GetPlaced());
        _ = await _rig.ProbeAsync<RecipeBook>(p, "Crafting", PlaceableOpCode.GetRecipeBook, "GetRecipeBook", "new player", () => new GetRecipeBook());
        _ = await _rig.ProbeAsync<FurnaceSnapshot>(p, "Crafting", PlaceableOpCode.GetFurnaces, "GetFurnaces", "no furnace", () => new GetFurnaces());
        _ = await _rig.ProbeAsync<WalletSnapshot>(p, "Market", MarketOpCode.GetWallet, "GetWallet", "wallet", () => new GetWallet());
        _ = await _rig.ProbeAsync<LooksSnapshot>(p, "Look", LookOpCode.GetLooks, "GetLooks", "own and seen players", () => new GetLooks());
        _ = await _rig.ProbeAsync<CoopSnapshot>(p, "Coop", CoopOpCode.GetCoop, "GetCoop", "no co-op", () => new GetCoop());
        _ = await _rig.ProbeAsync<GoalsSnapshot>(p, "Goals", GoalOpCode.GetGoals, "GetGoals", "goals", () => new GetGoals());
        _ = await _rig.ProbeAsync<FriendsSnapshot>(p, "Friends", FriendsOpCode.GetFriends, "GetFriends", "no friends", () => new GetFriends());
        _ = await _rig.ProbeAsync<MailList>(p, "Mail", MailOpCode.GetMail, "GetMail", "empty mailbox", () => new GetMail());
        _ = await _rig.ProbeAsync<BlockedList>(p, "Chat", ChatOpCode.GetBlocked, "GetBlocked", "no blocks", () => new GetBlocked());
        _ = await _rig.ProbeAsync<ListingsPage>(p, "Market", MarketOpCode.GetMyListings, "GetMyListings", "none", () => new GetMyListings());
        _ = await _rig.ProbeAsync<ListingsPage>(p, "Market", MarketOpCode.BrowseListings, "BrowseListings", "empty market, page 0", () => new BrowseListings { Page = 0 });
        _ = await _rig.ProbeAsync<PriceHistory>(p, "Market", MarketOpCode.GetPriceHistory, "GetPriceHistory", "no sales", () => new GetPriceHistory { ItemId = Nosum.Gameplay.Items.ItemCatalog.Parsnip });
        _ = await _rig.ProbeAsync<VisitorsSnapshot>(p, "Visit", VisitOpCode.GetVisitors, "GetVisitors", "no visitors", () => new GetVisitors());
        _ = await _rig.ProbeAsync<BuildingsSnapshot>(p, "Building", BuildingOpCode.GetBuildings, "GetBuildings", "own, none built", () => new GetBuildings { Mine = true });
        _ = await _rig.ProbeAsync<AnimalsSnapshot>(p, "Building", BuildingOpCode.GetAnimals, "GetAnimals", "not inside a building", () => new GetAnimals());

        // Walking: one legal step in place and back, as the client's 10 Hz reports.
        for (int i = 0; i < 2; i++)
        {
            await _rig.MeasureAsync("Player", PlayerOpCode.MoveIntent, "MoveIntent", "one step, same chunk", async () =>
            {
                await p.MoveByAsync(i == 0 ? 4 : -4, 0);
                return true;
            });
        }
    }
}
