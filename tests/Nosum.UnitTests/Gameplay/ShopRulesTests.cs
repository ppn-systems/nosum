// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
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
    public void GeneralStore_SellsSeedsFurnitureAndClothing_AtPositivePrices_AndNeverToolsOrProduce()
    {
        ShopCatalog.TryGet(ShopCatalog.GeneralStore, out ShopDefinition? shop).Should().BeTrue();

        shop!.Offers.Select(o => o.ItemId).Should().OnlyHaveUniqueItems();
        foreach (ShopOffer offer in shop.Offers)
        {
            offer.CoinsEach.Should().BePositive();
            ItemCatalog.TryGet(offer.ItemId, out ItemDefinition? item).Should().BeTrue($"item {offer.ItemId} must exist");
            item!.Kind.Should().BeOneOf(ItemKind.Seed, ItemKind.Furniture, ItemKind.Clothing);
        }

        // Everything the old starter kit gave away for free is for sale now.
        foreach (ItemDefinition item in ItemCatalog.All.Where(i => i.Kind is ItemKind.Furniture or ItemKind.Clothing))
        {
            shop.TryGetPrice(item.Id, out _).Should().BeTrue($"{item.Key} is bought at the shop");
        }
    }

    [Fact]
    public void Prices_AreSensibleAgainstTheStartingCoinsAndCropIncome()
    {
        ShopCatalog.TryGet(ShopCatalog.GeneralStore, out ShopDefinition? shop);

        // A seed costs less than its crop sells for at the bin, so farming pays.
        ShippingPrices.TryGet(ItemCatalog.Parsnip, out int parsnip);
        ShippingPrices.TryGet(ItemCatalog.Cauliflower, out int cauliflower);
        shop!.TryGetPrice(ItemCatalog.ParsnipSeeds, out int parsnipSeed);
        shop.TryGetPrice(ItemCatalog.CauliflowerSeeds, out int cauliflowerSeed);
        parsnipSeed.Should().BeLessThan(parsnip);
        cauliflowerSeed.Should().BeLessThan(cauliflower);

        // The starting 500 coins buy a chair, a plant and a lamp, but not the whole range.
        shop.TryGetPrice(ItemCatalog.OakChair, out int chair);
        shop.TryGetPrice(ItemCatalog.HousePlant, out int plant);
        shop.TryGetPrice(ItemCatalog.FloorLamp, out int lamp);
        (chair + plant + lamp).Should().BeLessThanOrEqualTo((int)CoinRules.StartingCoins);
        shop.Offers.Sum(o => (long)o.CoinsEach).Should().BeGreaterThan(CoinRules.StartingCoins);
    }

    [Fact]
    public void Price_ValidatesShopItemAndQuantity()
    {
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.OakChair, 3, out long total).Should().Be(ShopVerdict.Ok);
        total.Should().Be(300);
        ShopRules.Price(99, ItemCatalog.OakChair, 1, out _).Should().Be(ShopVerdict.UnknownShop);
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.Hoe, 1, out _).Should().Be(ShopVerdict.NotSold);
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.Parsnip, 1, out _).Should().Be(ShopVerdict.NotSold);
        ShopRules.Price(ShopCatalog.GeneralStore, 4242, 1, out _).Should().Be(ShopVerdict.NotSold);
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.OakChair, 0, out _).Should().Be(ShopVerdict.InvalidQuantity);
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.OakChair, -1, out _).Should().Be(ShopVerdict.InvalidQuantity);
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.ShirtRed, 2, out _).Should().Be(ShopVerdict.InvalidQuantity, "clothing stacks hold one");
        ShopRules.Price(ShopCatalog.GeneralStore, ItemCatalog.ParsnipSeeds, ItemCatalog.DefaultStack + 1, out _).Should().Be(ShopVerdict.InvalidQuantity);
    }

    [Fact]
    public void EveryShopPoint_NamesAShop_AndStandsOnAWalkableTile_OfAPublicMap()
    {
        MapSpec[] withShops = [.. GameMaps.All.Where(m => m.Shops.Count > 0)];
        withShops.Select(m => m.Id).Should().Equal([GameMaps.Town]);

        foreach (MapSpec spec in withShops)
        {
            spec.IsPrivate.Should().BeFalse("a shop is somewhere everyone can go");
            TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("assets", "Maps", spec.TmxFile)));
            WalkGrid grid = spec.BuildWalkGrid(map);
            foreach (ShopPoint point in spec.Shops)
            {
                ShopCatalog.TryGet(point.ShopId, out _).Should().BeTrue();
                grid.IsBlocked(point.TileX, point.TileY).Should().BeFalse("the player stands on the counter tile");
            }
        }
    }

    [Fact]
    public void TryFindShop_NearTheCounter_FindsItAndFarAway_DoesNot()
    {
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        ShopPoint point = town!.Shops[0];
        double cx = (point.TileX * 16) + 8;
        double cy = (point.TileY * 16) + 8;

        ShopRules.TryFindShop(town, cx, cy, 16, out int id).Should().BeTrue();
        id.Should().Be(ShopCatalog.GeneralStore);
        ShopRules.TryFindShop(town, cx + 30, cy, 16, out _).Should().BeTrue("within about two tiles");
        ShopRules.TryFindShop(town, cx + 100, cy, 16, out _).Should().BeFalse();
        GameMaps.TryGet(GameMaps.Farm, out MapSpec? farm);
        ShopRules.TryFindShop(farm!, cx, cy, 16, out _).Should().BeFalse("no shop on the farm");
    }
}
