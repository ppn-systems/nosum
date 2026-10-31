// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Shops;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The shop catalogue, its prices and where a shop is served (docs/adr/ADR-013 amendments).</summary>
public sealed class ShopRulesTests
{
    [Fact]
    public void Shops_AreFourWithStableIdsAndKeys()
    {
        ShopCatalog.All.Select(s => (s.Id, s.Key)).Should().Equal((1, "seed_shop"), (2, "clothing_shop"), (3, "furniture_shop"), (4, "carpenter_shop"), (5, "blacksmith"));
        ShopCatalog.SeedShop.Should().Be(1, "the town's only shop before the split keeps its id");
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop, ItemKind.Seed)]
    [InlineData(ShopCatalog.ClothingShop, ItemKind.Clothing)]
    [InlineData(ShopCatalog.FurnitureShop, ItemKind.Furniture)]
    [InlineData(ShopCatalog.Blacksmith, ItemKind.Resource)]
    public void EachShop_SellsOnlyItsOwnKindOfItem_AtPositivePrices_WithoutDuplicates(int shopId, ItemKind kind)
    {
        ShopCatalog.TryGet(shopId, out ShopDefinition? shop).Should().BeTrue();

        shop!.Offers.Should().NotBeEmpty();
        shop.Offers.Select(o => o.ItemId).Should().OnlyHaveUniqueItems();
        foreach (ShopOffer offer in shop.Offers)
        {
            offer.CoinsEach.Should().BePositive();
            ItemCatalog.TryGet(offer.ItemId, out ItemDefinition? item).Should().BeTrue($"item {offer.ItemId} must exist");

            // The tailor also sells the Rename Scroll, a paper item, not clothing (docs/adr/ADR-009 amendments).
            if (shopId == ShopCatalog.ClothingShop && offer.ItemId == ItemCatalog.RenameScroll)
            {
                continue;
            }

            item!.Kind.Should().Be(kind, $"{item.Key} does not belong in {shop.Key}");
        }
    }

    [Fact]
    public void TheTailor_SellsTheRenameScrollAt200Coins()
    {
        ShopRules.Price(ShopCatalog.ClothingShop, ItemCatalog.RenameScroll, 1, out long total).Should().Be(ShopVerdict.Ok);
        total.Should().Be(200);
    }

    [Fact]
    public void TheShopsTogether_SellEverySeedFurnitureAndClothingItem_ExactlyOnce()
    {
        // Everything the old starter kit gave away for free is for sale, and no item is sold by two shops.
        int[] sold = [.. ShopCatalog.All.SelectMany(s => s.Offers.Select(o => o.ItemId))];
        sold.Should().OnlyHaveUniqueItems();
        foreach (ItemDefinition item in ItemCatalog.All.Where(i => i.Kind is ItemKind.Furniture || (i.Kind is ItemKind.Clothing && !RecipeCatalog.All.Any(r => r.OutputItemId == i.Id) && i.Id != ItemCatalog.BossCrown)))
        {
            sold.Should().Contain(item.Id, $"{item.Key} is bought at a shop");
        }

        // The carpenter sells the milk pail, a tool that is not in the starter kit (docs/adr/ADR-028); every other tool and all produce stay unsold.
        foreach (int tool in ItemCatalog.All.Where(i => i.Kind is ItemKind.Tool or ItemKind.Produce && i.Id != ItemCatalog.MilkPail).Select(i => i.Id))
        {
            sold.Should().NotContain(tool, "tools and produce are never sold");
        }
    }

    [Fact]
    public void Prices_AreUnchangedBySplittingTheShop()
    {
        (int Shop, int Item, int Coins)[] expected =
        [
            (ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 10), (ShopCatalog.SeedShop, ItemCatalog.RadishSeeds, 15), (ShopCatalog.SeedShop, ItemCatalog.CauliflowerSeeds, 30),
            (ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 100), (ShopCatalog.FurnitureShop, ItemCatalog.HousePlant, 80), (ShopCatalog.FurnitureShop, ItemCatalog.FloorLamp, 140),
            (ShopCatalog.FurnitureShop, ItemCatalog.OakTable, 220), (ShopCatalog.FurnitureShop, ItemCatalog.Bookcase, 260), (ShopCatalog.FurnitureShop, ItemCatalog.BlueArmchair, 300),
            (ShopCatalog.FurnitureShop, ItemCatalog.BlueCouch, 400),
            (ShopCatalog.ClothingShop, ItemCatalog.ShirtRed, 60), (ShopCatalog.ClothingShop, ItemCatalog.ShirtGreen, 60), (ShopCatalog.ClothingShop, ItemCatalog.ShirtBlue, 60),
            (ShopCatalog.ClothingShop, ItemCatalog.PantsBlue, 80), (ShopCatalog.ClothingShop, ItemCatalog.PantsKhaki, 80), (ShopCatalog.ClothingShop, ItemCatalog.HatStraw, 50),
            (ShopCatalog.ClothingShop, ItemCatalog.HatCap, 40),
        ];

        foreach ((int shop, int item, int coins) in expected)
        {
            ShopRules.Price(shop, item, 1, out long total).Should().Be(ShopVerdict.Ok);
            total.Should().Be(coins);
        }
    }

    [Fact]
    public void Prices_AreSensibleAgainstTheStartingCoinsAndCropIncome()
    {
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? seeds);
        ShopCatalog.TryGet(ShopCatalog.FurnitureShop, out ShopDefinition? furniture);

        // A seed costs less than its crop sells for at the bin, so farming pays.
        ShippingPrices.TryGet(ItemCatalog.Parsnip, out int parsnip);
        ShippingPrices.TryGet(ItemCatalog.Cauliflower, out int cauliflower);
        seeds!.TryGetPrice(ItemCatalog.ParsnipSeeds, out int parsnipSeed);
        seeds.TryGetPrice(ItemCatalog.CauliflowerSeeds, out int cauliflowerSeed);
        parsnipSeed.Should().BeLessThan(parsnip);
        cauliflowerSeed.Should().BeLessThan(cauliflower);

        // The starting 500 coins buy a chair, a plant and a lamp, but not the whole range.
        furniture!.TryGetPrice(ItemCatalog.OakChair, out int chair);
        furniture.TryGetPrice(ItemCatalog.HousePlant, out int plant);
        furniture.TryGetPrice(ItemCatalog.FloorLamp, out int lamp);
        (chair + plant + lamp).Should().BeLessThanOrEqualTo((int)CoinRules.StartingCoins);
        ShopCatalog.All.SelectMany(s => s.Offers).Sum(o => (long)o.CoinsEach).Should().BeGreaterThan(CoinRules.StartingCoins);
    }

    [Fact]
    public void Price_ValidatesShopItemAndQuantity()
    {
        ShopRules.Price(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 3, out long total).Should().Be(ShopVerdict.Ok);
        total.Should().Be(300);
        ShopRules.Price(99, ItemCatalog.OakChair, 1, out _).Should().Be(ShopVerdict.UnknownShop);
        ShopRules.Price(ShopCatalog.FurnitureShop, ItemCatalog.Hoe, 1, out _).Should().Be(ShopVerdict.NotSold);
        ShopRules.Price(ShopCatalog.SeedShop, ItemCatalog.Parsnip, 1, out _).Should().Be(ShopVerdict.NotSold);
        ShopRules.Price(ShopCatalog.FurnitureShop, 4242, 1, out _).Should().Be(ShopVerdict.NotSold);
        ShopRules.Price(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 0, out _).Should().Be(ShopVerdict.InvalidQuantity);
        ShopRules.Price(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, -1, out _).Should().Be(ShopVerdict.InvalidQuantity);
        ShopRules.Price(ShopCatalog.ClothingShop, ItemCatalog.ShirtRed, 2, out _).Should().Be(ShopVerdict.InvalidQuantity, "clothing stacks hold one");
        ShopRules.Price(ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, ItemCatalog.DefaultStack + 1, out _).Should().Be(ShopVerdict.InvalidQuantity);
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.OakChair)]
    [InlineData(ShopCatalog.SeedShop, ItemCatalog.ShirtRed)]
    [InlineData(ShopCatalog.ClothingShop, ItemCatalog.ParsnipSeeds)]
    [InlineData(ShopCatalog.ClothingShop, ItemCatalog.OakChair)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.ParsnipSeeds)]
    [InlineData(ShopCatalog.FurnitureShop, ItemCatalog.HatCap)]
    public void Price_AnItemOfAnotherShop_IsNotSold(int shopId, int itemId)
        => ShopRules.Price(shopId, itemId, 1, out _).Should().Be(ShopVerdict.NotSold);

    [Fact]
    public void TownHasOneCounterPerShop_AtTheRecordedTiles_AndNoOtherMapHasAny()
    {
        GameMaps.All.Where(m => m.Shops.Count > 0).Select(m => m.Id).Should().Equal([GameMaps.Town]);
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town);

        town!.Shops.Select(p => (p.ShopId, p.TileX, p.TileY)).Should().Equal(
            (ShopCatalog.SeedShop, 43, 57),
            (ShopCatalog.ClothingShop, 20, 89),
            (ShopCatalog.FurnitureShop, 95, 51),
            (ShopCatalog.Blacksmith, 94, 82),
            (ShopCatalog.CarpenterShop, 10, 86),
            (CommunityGoalCatalog.BoardCounterId, 42, 57),
            (MarketRules.MarketCounterId, 45, 71));
        foreach (ShopDefinition shop in ShopCatalog.All)
        {
            town.Shops.Count(p => p.ShopId == shop.Id).Should().Be(1, $"{shop.Key} has one counter");
        }
    }

    [Fact]
    public void EveryShopPoint_NamesAShop_StandsOnAWalkableTile_ReachableOnFootFromTheSpawn_InFrontOfADoor()
    {
        MapSpec[] withShops = [.. GameMaps.All.Where(m => m.Shops.Count > 0)];

        foreach (MapSpec spec in withShops)
        {
            spec.IsPrivate.Should().BeFalse("a shop is somewhere everyone can go");
            TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", spec.TmxFile)));
            WalkGrid grid = spec.BuildWalkGrid(map);
            bool[] reachable = ReachableFrom(grid, spec.SpawnTileX, spec.SpawnTileY);
            foreach (ShopPoint point in spec.Shops)
            {
                (point.ShopId is CommunityGoalCatalog.BoardCounterId or MarketRules.MarketCounterId || ShopCatalog.TryGet(point.ShopId, out _)).Should().BeTrue("a counter is a shop, the community board or the market");
                grid.IsBlocked(point.TileX, point.TileY).Should().BeFalse("the player stands on the counter tile");
                reachable[(point.TileY * grid.Width) + point.TileX].Should().BeTrue($"shop {point.ShopId} must be walkable to from the spawn");
                grid.IsBlocked(point.TileX, point.TileY - 1).Should().BeTrue("the counter is the doormat just south of the building's door");
            }
        }
    }

    [Fact]
    public void StandingAtACounter_UsesThatCounter_AndOnlyTheBoardAndTheSeedShopAreCloseEnoughToShareAPlace()
    {
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        foreach (ShopPoint point in town!.Shops)
        {
            double cx = (point.TileX * 16) + 8;
            double cy = (point.TileY * 16) + 8;
            ShopRules.TryFindShop(town, cx, cy, 16, out int id).Should().BeTrue();
            id.Should().Be(point.ShopId, "the nearest counter wins, so each counter's own tile uses it");

            // Two counters in reach of one place is allowed for the board and the seed shop only (the billboard stands on the seed shop's store).
            int[] reached = [.. town.Shops.Where(other => ShopRules.InReach(other, cx, cy, 16)).Select(other => other.ShopId)];
            if (reached.Length > 1)
            {
                reached.Should().BeEquivalentTo([ShopCatalog.SeedShop, CommunityGoalCatalog.BoardCounterId]);
            }
        }
    }

    [Fact]
    public void TheMarketCounter_IsOneTileInTown_HasItsOwnId_AndIsAloneInItsReach()
    {
        MarketRules.MarketCounterId.Should().NotBe(CommunityGoalCatalog.BoardCounterId).And.BeGreaterThan(CommunityGoalCatalog.BoardCounterId);
        ShopCatalog.TryGet(MarketRules.MarketCounterId, out _).Should().BeFalse("the market is not a shop");
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        ShopPoint market = town!.Shops.Single(p => p.ShopId == MarketRules.MarketCounterId);
        double cx = (market.TileX * 16) + 8;
        double cy = (market.TileY * 16) + 8;

        ShopRules.IsAtCounter(town, MarketRules.MarketCounterId, cx, cy, 16).Should().BeTrue();
        ShopRules.IsAtCounter(town, MarketRules.MarketCounterId, cx + 30, cy, 16).Should().BeTrue("within about two tiles, like the shops");
        ShopRules.IsAtCounter(town, MarketRules.MarketCounterId, cx + 100, cy, 16).Should().BeFalse();
        ShopRules.IsAtCounter(town, MarketRules.MarketCounterId, (town.SpawnTileX * 16) + 8, (town.SpawnTileY * 16) + 8, 16).Should().BeFalse("the Town spawn is not at the counter");
        town.Shops.Where(other => other.ShopId != MarketRules.MarketCounterId && ShopRules.InReach(other, cx, cy, 16)).Should().BeEmpty("no other counter competes for the market's place");
        foreach (ShopPoint other in town.Shops.Where(p => p.ShopId != MarketRules.MarketCounterId))
        {
            ShopRules.IsAtCounter(town, MarketRules.MarketCounterId, (other.TileX * 16) + 8, (other.TileY * 16) + 8, 16).Should().BeFalse($"standing at counter {other.ShopId} is not standing at the market");
        }

        GameMaps.TryGet(GameMaps.Farm, out MapSpec? farm);
        ShopRules.IsAtCounter(farm!, MarketRules.MarketCounterId, cx, cy, 16).Should().BeFalse("no market outside Town");
    }

    [Fact]
    public void TheBoardAndTheSeedShop_EachWinTheirOwnSideOfTheStoreFront()
    {
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town);

        ShopRules.TryFindShop(town!, (41 * 16) + 8, (57 * 16) + 8, 16, out int west).Should().BeTrue();
        ShopRules.TryFindShop(town!, (42 * 16) + 8, (57 * 16) + 8, 16, out int board).Should().BeTrue();
        ShopRules.TryFindShop(town!, (43 * 16) + 8, (57 * 16) + 8, 16, out int seeds).Should().BeTrue();
        ShopRules.TryFindShop(town!, (44 * 16) + 8, (57 * 16) + 8, 16, out int east).Should().BeTrue();

        (west, board, seeds, east).Should().Be((CommunityGoalCatalog.BoardCounterId, CommunityGoalCatalog.BoardCounterId, ShopCatalog.SeedShop, ShopCatalog.SeedShop));
        ShopRules.IsAtCounter(town!, CommunityGoalCatalog.BoardCounterId, (42 * 16) + 8, (57 * 16) + 8, 16).Should().BeTrue();
        ShopRules.IsAtCounter(town!, CommunityGoalCatalog.BoardCounterId, (43 * 16) + 8, (57 * 16) + 8, 16).Should().BeFalse();
    }

    [Fact]
    public void TryFindShop_NearEachCounter_FindsThatShop_AndFarAway_DoesNot()
    {
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        foreach (ShopPoint point in town!.Shops)
        {
            double cx = (point.TileX * 16) + 8;
            double cy = (point.TileY * 16) + 8;

            ShopRules.TryFindShop(town, cx, cy, 16, out int id).Should().BeTrue();
            id.Should().Be(point.ShopId);
            ShopRules.TryFindShop(town, cx + 30, cy, 16, out _).Should().BeTrue("within about two tiles");
            ShopRules.TryFindShop(town, cx + 100, cy, 16, out _).Should().BeFalse();
        }

        ShopPoint first = town.Shops[0];
        GameMaps.TryGet(GameMaps.Farm, out MapSpec? farm);
        ShopRules.TryFindShop(farm!, (first.TileX * 16) + 8, (first.TileY * 16) + 8, 16, out _).Should().BeFalse("no shop on the farm");
    }

    private static bool[] ReachableFrom(WalkGrid grid, int startX, int startY)
    {
        bool[] seen = new bool[grid.Width * grid.Height];
        Queue<(int X, int Y)> queue = new();
        queue.Enqueue((startX, startY));
        seen[(startY * grid.Width) + startX] = true;
        (int Dx, int Dy)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in steps)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height || seen[(ny * grid.Width) + nx] || grid.IsBlocked(nx, ny))
                {
                    continue;
                }

                seen[(ny * grid.Width) + nx] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return seen;
    }

    [Fact]
    public void IsInSeason_SeedOnlyInTheSeasonsItsCropGrows_EverythingElseAlways()
    {
        GameCalendar calendar = GameCalendar.Default;
        long spring = calendar.EpochMs;
        long summer = spring + calendar.SeasonLengthMs;

        ShopRules.IsInSeason(ItemCatalog.TomatoSeeds, calendar, spring).Should().BeFalse();
        ShopRules.IsInSeason(ItemCatalog.TomatoSeeds, calendar, summer).Should().BeTrue();
        ShopRules.IsInSeason(ItemCatalog.CauliflowerSeeds, calendar, summer).Should().BeTrue();
        ShopRules.IsInSeason(ItemCatalog.OakChair, calendar, spring).Should().BeTrue();
        ShopRules.IsInSeason(ItemCatalog.HatCap, calendar, summer).Should().BeTrue();
        ShopRules.IsInSeason(999_999, calendar, spring).Should().BeTrue("unknown ids are the catalogue's business, not the season's");
    }
    [Fact]
    public void IsInSeason_ShopRotation_StrawHatsInTheSunnyHalfAndCapsInTheColdHalf()
    {
        GameCalendar calendar = GameCalendar.Default;
        long spring = calendar.EpochMs;
        long summer = spring + calendar.SeasonLengthMs;
        long fall = spring + (2 * calendar.SeasonLengthMs);
        long winter = spring + (3 * calendar.SeasonLengthMs);

        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatStraw, calendar, spring).Should().BeTrue();
        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatStraw, calendar, summer).Should().BeTrue();
        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatStraw, calendar, fall).Should().BeFalse();
        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatCap, calendar, spring).Should().BeFalse();
        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatCap, calendar, fall).Should().BeTrue();
        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatCap, calendar, winter).Should().BeTrue();
    }

    [Fact]
    public void IsInSeason_ShopRotation_OnlyNarrowsItsOwnCounter()
    {
        GameCalendar calendar = GameCalendar.Default;
        long spring = calendar.EpochMs;
        long fall = spring + (2 * calendar.SeasonLengthMs);

        ShopRules.IsInSeason(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, calendar, fall).Should().BeTrue("a year-round piece stays on every counter");
        ShopRules.IsInSeason(ShopCatalog.SeedShop, ItemCatalog.TomatoSeeds, calendar, spring).Should().BeFalse("the seed rule still applies at the seed counter");
        ShopRules.IsInSeason(ShopCatalog.ClothingShop, ItemCatalog.HatCap, calendar, fall).Should().BeTrue();
        ShopRules.IsInSeason(ShopCatalog.FurnitureShop, ItemCatalog.HatCap, calendar, spring).Should().BeTrue("a hat is only held back by the clothing counter");
    }

    [Fact]
    public void EveryShop_HasSomethingInStockInEverySeason()
    {
        GameCalendar calendar = GameCalendar.Default;
        for (int season = 0; season < GameCalendar.SeasonsPerYear; season++)
        {
            long now = calendar.EpochMs + (season * calendar.SeasonLengthMs);
            foreach (ShopDefinition shop in ShopCatalog.All)
            {
                shop.Offers.Any(offer => ShopRules.IsInSeason(shop.Id, offer.ItemId, calendar, now))
                    .Should().BeTrue($"{shop.Key} has no stock in season {season}");
            }
        }
    }

    [Fact]
    public void Price_OfASeasonalHat_ReadsTheCatalogue_TheSeasonIsCheckedSeparately()
    {
        ShopRules.Price(ShopCatalog.ClothingShop, ItemCatalog.HatCap, 1, out long total).Should().Be(ShopVerdict.Ok);
        total.Should().Be(40);
    }
}
