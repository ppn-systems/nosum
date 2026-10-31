// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
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
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Pets;
using Nosum.Gameplay.Shops;
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
        _ = await _rig.ProbeAsync<InventorySnapshot>(p, "Inventory", InventoryOpCode.GetInventory, "GetInventory", "starter kit, asked again", () => new GetInventory());
        _ = await _rig.ProbeAsync<InventoryResult>(p, "Inventory", InventoryOpCode.SelectSlot, "SelectSlot", "hotbar slot 1", () => new SelectSlot { Slot = 1 });
        using (SelectSlot choice = new() { Slot = 2 })
        {
            _ = await p.RequestAsync<InventoryResult>(choice);
        }

        _ = await _rig.MeasureAsync("Background", null, "WriteBehindFlush", "a hotbar choice waiting", async () =>
        {
            await BudgetRig.Service<Nosum.Infrastructure.Caching.WriteBehind>().FlushAllAsync(_rig.Timeout);
            return true;
        });
        _ = await _rig.ProbeAsync<InventoryResult>(p, "Inventory", InventoryOpCode.MoveSlot, "MoveSlot", "split a stack into a free slot", () => new MoveSlot { From = 2, To = 20, Quantity = 1 });
        _ = await _rig.ProbeAsync<InventoryResult>(p, "Inventory", InventoryOpCode.SortInventory, "SortInventory", "sort a bag", () => new SortInventory());
        _ = await _rig.ProbeAsync<FarmSnapshot>(p, "Farm", FarmOpCode.GetFarm, "GetFarm", "empty farm", () => new GetFarm());
        _ = await _rig.ProbeAsync<FarmSnapshot>(p, "Farm", FarmOpCode.GetFarm, "GetFarm", "empty farm, asked again", () => new GetFarm());
        _ = await _rig.ProbeAsync<NodeSnapshot>(p, "Node", NodeOpCode.GetNodes, "GetNodes", "farm", () => new GetNodes());
        _ = await _rig.ProbeAsync<NodeSnapshot>(p, "Node", NodeOpCode.GetNodes, "GetNodes", "farm, asked again", () => new GetNodes());
        _ = await _rig.ProbeAsync<GroundItemsSnapshot>(p, "GroundItem", GroundItemOpCode.GetGroundItems, "GetGroundItems", "nothing on the ground", () => new GetGroundItems());
        _ = await _rig.ProbeAsync<GroundItemsSnapshot>(p, "GroundItem", GroundItemOpCode.GetGroundItems, "GetGroundItems", "nothing on the ground, asked again", () => new GetGroundItems());
        _ = await _rig.ProbeAsync<QuestLog>(p, "Quest", QuestOpCode.GetQuests, "GetQuests", "new player", () => new GetQuests());
        _ = await _rig.ProbeAsync<QuestLog>(p, "Quest", QuestOpCode.GetQuests, "GetQuests", "new player, asked again", () => new GetQuests());
        _ = await _rig.ProbeAsync<CalendarInfo>(p, "Calendar", SeasonOpCode.GetCalendar, "GetCalendar", "calendar", () => new GetCalendar());
        _ = await _rig.ProbeAsync<CalendarInfo>(p, "Calendar", SeasonOpCode.GetCalendar, "GetCalendar", "calendar, asked again", () => new GetCalendar());
        _ = await _rig.ProbeAsync<PlacedSnapshot>(p, "Placeable", PlaceableOpCode.GetPlaced, "GetPlaced", "empty farm", () => new GetPlaced());
        _ = await _rig.ProbeAsync<PlacedSnapshot>(p, "Placeable", PlaceableOpCode.GetPlaced, "GetPlaced", "empty farm, asked again", () => new GetPlaced());
        _ = await _rig.ProbeAsync<RecipeBook>(p, "Crafting", PlaceableOpCode.GetRecipeBook, "GetRecipeBook", "new player", () => new GetRecipeBook());
        _ = await _rig.ProbeAsync<RecipeBook>(p, "Crafting", PlaceableOpCode.GetRecipeBook, "GetRecipeBook", "new player, asked again", () => new GetRecipeBook());
        _ = await _rig.ProbeAsync<FurnaceSnapshot>(p, "Crafting", PlaceableOpCode.GetFurnaces, "GetFurnaces", "no furnace", () => new GetFurnaces());
        _ = await _rig.ProbeAsync<FurnaceSnapshot>(p, "Crafting", PlaceableOpCode.GetFurnaces, "GetFurnaces", "no furnace, asked again", () => new GetFurnaces());
        _ = await _rig.ProbeAsync<WalletSnapshot>(p, "Market", MarketOpCode.GetWallet, "GetWallet", "wallet", () => new GetWallet());
        _ = await _rig.ProbeAsync<WalletSnapshot>(p, "Market", MarketOpCode.GetWallet, "GetWallet", "wallet, asked again", () => new GetWallet());
        _ = await _rig.ProbeAsync<LooksSnapshot>(p, "Look", LookOpCode.GetLooks, "GetLooks", "own and seen players", () => new GetLooks());
        _ = await _rig.ProbeAsync<LooksSnapshot>(p, "Look", LookOpCode.GetLooks, "GetLooks", "own and seen players, asked again", () => new GetLooks());
        _ = await _rig.ProbeAsync<PetState>(p, "Pet", PetOpCode.GetPet, "GetPet", "no pet", () => new GetPet());
        _ = await _rig.ProbeAsync<PetState>(p, "Pet", PetOpCode.GetPet, "GetPet", "no pet, asked again", () => new GetPet());
        // Pets are bought and given back at the carpenter's counter in the Town (docs/adr/ADR-075 amendment): a farmer stands there with the coins.
        ShopPoint carpenter = GameMaps.All.Single(m => m.Id == GameMaps.Town).Shops.Single(c => c.ShopId == ShopCatalog.CarpenterShop);
        (double carpenterX, double carpenterY) = TileCentre(carpenter.TileX, carpenter.TileY);
        await using BudgetPlayer petBuyer = await this.FreshAsync(GameMaps.Town, carpenterX, carpenterY, 2_000);
        _ = await _rig.ProbeAsync<PetState>(petBuyer, "Pet", PetOpCode.AdoptPet, "AdoptPet", "adopt a cat", () => new AdoptPet { Kind = (byte)PetKind.Cat, Name = "Mochi" });
        _ = await _rig.ProbeAsync<PetState>(petBuyer, "Pet", PetOpCode.PetPet, "PetPet", "pet the cat", () => new PetPet());
        _ = await _rig.ProbeAsync<PetState>(petBuyer, "Pet", PetOpCode.PetPet, "PetPet", "pet the cat, asked again", () => new PetPet());
        _ = await _rig.ProbeAsync<PetState>(petBuyer, "Pet", PetOpCode.RehomePet, "RehomePet", "give the cat back", () => new RehomePet());
        _ = await _rig.ProbeAsync<EmoteResult>(p, "Emote", EmoteOpCode.SendEmote, "SendEmote", "wave", () => new SendEmote { Emote = 2 });
        _ = await _rig.ProbeAsync<CoopSnapshot>(p, "Coop", CoopOpCode.GetCoop, "GetCoop", "no co-op", () => new GetCoop());
        _ = await _rig.ProbeAsync<CoopSnapshot>(p, "Coop", CoopOpCode.GetCoop, "GetCoop", "no co-op, asked again", () => new GetCoop());
        _ = await _rig.ProbeAsync<GoalsSnapshot>(p, "Goals", GoalOpCode.GetGoals, "GetGoals", "goals", () => new GetGoals());
        _ = await _rig.ProbeAsync<GoalsSnapshot>(p, "Goals", GoalOpCode.GetGoals, "GetGoals", "goals, asked again", () => new GetGoals());
        _ = await _rig.ProbeAsync<FriendsSnapshot>(p, "Friends", FriendsOpCode.GetFriends, "GetFriends", "no friends", () => new GetFriends());
        _ = await _rig.ProbeAsync<FriendsSnapshot>(p, "Friends", FriendsOpCode.GetFriends, "GetFriends", "no friends, asked again", () => new GetFriends());
        _ = await _rig.ProbeAsync<MailList>(p, "Mail", MailOpCode.GetMail, "GetMail", "empty mailbox", () => new GetMail());
        _ = await _rig.ProbeAsync<MailList>(p, "Mail", MailOpCode.GetMail, "GetMail", "empty mailbox, asked again", () => new GetMail());
        _ = await _rig.ProbeAsync<BlockedList>(p, "Chat", ChatOpCode.GetBlocked, "GetBlocked", "no blocks", () => new GetBlocked());
        _ = await _rig.ProbeAsync<BlockedList>(p, "Chat", ChatOpCode.GetBlocked, "GetBlocked", "no blocks, asked again", () => new GetBlocked());
        _ = await _rig.ProbeAsync<ListingsPage>(p, "Market", MarketOpCode.GetMyListings, "GetMyListings", "none", () => new GetMyListings());
        _ = await _rig.ProbeAsync<ListingsPage>(p, "Market", MarketOpCode.GetMyListings, "GetMyListings", "none, asked again", () => new GetMyListings());
        _ = await _rig.ProbeAsync<ListingsPage>(p, "Market", MarketOpCode.BrowseListings, "BrowseListings", "empty market, page 0", () => new BrowseListings { Page = 0 });
        _ = await _rig.ProbeAsync<ListingsPage>(p, "Market", MarketOpCode.BrowseListings, "BrowseListings", "empty market, page 0, asked again", () => new BrowseListings { Page = 0 });
        _ = await _rig.ProbeAsync<PriceHistory>(p, "Market", MarketOpCode.GetPriceHistory, "GetPriceHistory", "no sales", () => new GetPriceHistory { ItemId = Nosum.Gameplay.Items.ItemCatalog.Parsnip });
        _ = await _rig.ProbeAsync<PriceHistory>(p, "Market", MarketOpCode.GetPriceHistory, "GetPriceHistory", "no sales, asked again", () => new GetPriceHistory { ItemId = Nosum.Gameplay.Items.ItemCatalog.Parsnip });
        _ = await _rig.ProbeAsync<VisitorsSnapshot>(p, "Visit", VisitOpCode.GetVisitors, "GetVisitors", "no visitors", () => new GetVisitors());
        _ = await _rig.ProbeAsync<VisitorsSnapshot>(p, "Visit", VisitOpCode.GetVisitors, "GetVisitors", "no visitors, asked again", () => new GetVisitors());
        _ = await _rig.ProbeAsync<BuildingsSnapshot>(p, "Building", BuildingOpCode.GetBuildings, "GetBuildings", "own, none built", () => new GetBuildings { Mine = true });
        _ = await _rig.ProbeAsync<BuildingsSnapshot>(p, "Building", BuildingOpCode.GetBuildings, "GetBuildings", "own, none built, asked again", () => new GetBuildings { Mine = true });
        _ = await _rig.ProbeAsync<AnimalsSnapshot>(p, "Building", BuildingOpCode.GetAnimals, "GetAnimals", "not inside a building", () => new GetAnimals());
        _ = await _rig.ProbeAsync<AnimalsSnapshot>(p, "Building", BuildingOpCode.GetAnimals, "GetAnimals", "not inside a building, asked again", () => new GetAnimals());

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
