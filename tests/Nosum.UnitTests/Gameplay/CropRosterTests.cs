// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The crop roster and the regrowing crops (docs/adr/ADR-025 D1, D2, D5, D6).</summary>
public sealed class CropRosterTests
{
    // crops.png is 256 x 352: two rows of eight 16 x 32 frames per 32 px band, 21 crop rows in catalogue order. items.png is 24 x 6 cells.
    private const int CropSheetRows = 22;
    private const int ObjectCells = 24 * 6;

    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    [Fact]
    public void Roster_HasTheTwentyOneCropsOfTheDecision_InIdOrder()
    {
        CropCatalog.All.Select(c => c.Key).Should().Equal(
            "parsnip", "radish", "cauliflower", "potato", "green_bean", "kale", "garlic", "strawberry",
            "tomato", "blueberry", "melon", "hot_pepper", "corn", "wheat", "sunflower",
            "pumpkin", "cranberries", "eggplant", "yam", "bok_choy", "grape");
        CropCatalog.All.Select(c => c.Id).Should().Equal(Enumerable.Range(1, 21));
    }

    [Theory]
    [InlineData(Season.Spring, new[] { "parsnip", "radish", "cauliflower", "potato", "green_bean", "kale", "garlic", "strawberry" })]
    [InlineData(Season.Summer, new[] { "radish", "cauliflower", "tomato", "blueberry", "melon", "hot_pepper", "corn", "wheat", "sunflower" })]
    [InlineData(Season.Fall, new[] { "parsnip", "radish", "corn", "wheat", "pumpkin", "cranberries", "eggplant", "yam", "bok_choy", "grape" })]
    [InlineData(Season.Winter, new[] { "parsnip" })]
    public void Roster_EachSeasonGrowsTheCropsOfTheDecision(Season season, string[] keys)
        => CropCatalog.All.Where(c => c.GrowsIn(season)).Select(c => c.Key).Should().BeEquivalentTo(keys);

    [Fact]
    public void Roster_RegrowingCrops_AreTheOnesOfTheDecision()
        => CropCatalog.All.Where(c => c.Regrows).Select(c => c.Key).Should().BeEquivalentTo(
            "green_bean", "strawberry", "tomato", "blueberry", "hot_pepper", "corn", "cranberries", "eggplant", "grape");

    [Fact]
    public void EveryCrop_HasASeasonASeedAndProduceItem_AndASeedInTheSeedShop()
    {
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop).Should().BeTrue();

        foreach (CropDefinition crop in CropCatalog.All)
        {
            crop.Seasons.Should().NotBe(SeasonSet.None, crop.Key);
            ItemCatalog.All.Should().ContainSingle(i => i.Kind == ItemKind.Seed && i.CropId == crop.Id, crop.Key);
            ItemDefinition seed = ItemCatalog.All.Single(i => i.Kind == ItemKind.Seed && i.CropId == crop.Id);
            shop!.TryGetPrice(seed.Id, out int seedPrice).Should().BeTrue(crop.Key);
            seedPrice.Should().BeGreaterThan(0);
            ItemCatalog.TryGet(crop.HarvestItemId, out ItemDefinition? produce).Should().BeTrue(crop.Key);
            produce!.Kind.Should().Be(ItemKind.Produce);
            ShippingPrices.TryGet(produce.Id, out int sells).Should().BeTrue(crop.Key);
            sells.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void SeedPrices_EveryFirstHarvestPaysItsSeed_SingleHarvestSeedsAreAFifthToHalfOfIt()
    {
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop);

        foreach (CropDefinition crop in CropCatalog.All)
        {
            int seed = shop!.Offers.Single(o => ItemCatalog.TryGet(o.ItemId, out ItemDefinition? i) && i.CropId == crop.Id).CoinsEach;
            ShippingPrices.TryGet(crop.HarvestItemId, out int sell);
            double value = sell * crop.HarvestQuantity;
            seed.Should().BeLessThan((int)value, crop.Key);
            if (!crop.Regrows)
            {
                seed.Should().BeInRange((int)(value * 0.2), (int)(value * 0.5), crop.Key);
            }
        }
    }

    [Fact]
    public void Sprites_EveryCropHasAFramePerStage_InsideTheCropSheet()
    {
        foreach (CropDefinition crop in CropCatalog.All)
        {
            crop.SpriteRow.Should().BeInRange(0, CropSheetRows - 1, crop.Key);
            crop.SpriteFrames.Should().HaveCount(crop.StageCount, crop.Key);
            crop.SpriteFrames.Should().OnlyContain(f => f >= 0 && f < 8, crop.Key);
        }

        CropCatalog.All.Select(c => c.SpriteRow).Should().OnlyHaveUniqueItems("every crop owns its sheet row");
    }

    [Fact]
    public void Sprites_EveryItemIconCell_IsInsideTheObjectsSheet()
    {
        foreach (ItemDefinition item in ItemCatalog.All.Where(i => i.ObjectIcon != 0))
        {
            item.ObjectIcon.Should().BeInRange(1, ObjectCells - 1, item.Key);
        }

        foreach (ItemDefinition item in ItemCatalog.All.Where(i => i.Kind is ItemKind.Seed or ItemKind.Produce))
        {
            item.ObjectIcon.Should().BeGreaterThan(0, $"{item.Key} is pictured on the objects sheet");
        }
    }

    [Fact]
    public void Regrow_EveryRegrowingCrop_ReturnsToItsLastGrowingStage_ShowingTheFruitlessPlant()
    {
        foreach (CropDefinition crop in CropCatalog.All.Where(c => c.Regrows))
        {
            crop.RegrowDays.Should().BeLessThan(crop.TotalGrowthDays, crop.Key);
            crop.StageAt(crop.GrowthDaysAfterHarvest).Should().Be(crop.StageCount - 2, crop.Key);
            crop.SpriteFrames[crop.StageCount - 2].Should().Be(7, $"{crop.Key}: frame 7 of the sheet row is the plant without fruit");
        }
    }

    [Fact]
    public void Harvest_RegrowingCrop_KeepsThePlantAndNeedsItsRegrowDays()
    {
        CropDefinition tomato = CropCatalog.All.Single(c => c.Id == CropCatalog.Tomato);
        long summer = s_calendar.EpochMs + s_calendar.SeasonLengthMs;
        int day = s_calendar.DayNumberAt(summer);
        FarmTile ripe = new(CropCatalog.Tomato, summer, tomato.TotalGrowthDays, 0, 0);

        FarmRules.Harvest(ripe, Noon(day), s_calendar, out FarmTile next, out CropDefinition? crop).Should().Be(FarmVerdict.Ok);

        crop.Should().BeSameAs(tomato);
        next.CropId.Should().Be(CropCatalog.Tomato, "the plant stays");
        next.PlantedAtMs.Should().Be(Noon(day), "regrowth counts from the harvest (docs/adr/ADR-027 D2)");
        long fall = summer + s_calendar.SeasonLengthMs;
        long winter = fall + s_calendar.SeasonLengthMs;
        FarmRules.IsWithered(next, winter + 1, s_calendar).Should().Be(FarmRules.IsWithered(ripe, winter + 1, s_calendar), "it withers with its season, as before");
        FarmRules.IsWithered(next, fall - 1, s_calendar).Should().Be(FarmRules.IsWithered(ripe, fall - 1, s_calendar));
        next.GrowthDays.Should().Be(tomato.TotalGrowthDays - tomato.RegrowDays);
        FarmRules.IsMature(next, Noon(day), s_calendar).Should().BeFalse();
        FarmRules.StageAt(next, Noon(day), s_calendar).Should().Be(tomato.StageCount - 2);
    }

    [Fact]
    public void Harvest_RegrowingCrop_BearsAgainAfterExactlyItsRegrowDaysOfWatering()
    {
        // Indoors, so rain never waters the plant: only the hand watering below counts (docs/adr/ADR-027 D2).
        CropDefinition bean = CropCatalog.All.Single(c => c.Id == CropCatalog.GreenBean);
        int start = 2;
        FarmRules.Plant(FarmTile.Bare, CropCatalog.GreenBean, Noon(start), s_calendar, out FarmTile tile).Should().Be(FarmVerdict.Ok);
        int day = start;
        for (int watered = 0; watered < bean.TotalGrowthDays; watered++, day++)
        {
            FarmRules.Water(tile, Noon(day), s_calendar, out tile, outdoors: false).Should().Be(FarmVerdict.Ok);
        }

        FarmRules.IsMature(tile, Midnight(day), s_calendar, outdoors: false).Should().BeTrue("the first crop is ready");
        FarmRules.Harvest(tile, Midnight(day), s_calendar, out tile, out _, outdoors: false).Should().Be(FarmVerdict.Ok);

        for (int watered = 0; watered < bean.RegrowDays; watered++, day++)
        {
            FarmRules.IsMature(tile, Midnight(day), s_calendar, outdoors: false).Should().BeFalse($"{watered} of {bean.RegrowDays} regrow days");
            FarmRules.Water(tile, Noon(day), s_calendar, out tile, outdoors: false).Should().Be(FarmVerdict.Ok);
        }

        FarmRules.IsMature(tile, Midnight(day), s_calendar, outdoors: false).Should().BeTrue("bears again after its regrow days");
        FarmRules.Harvest(tile, Midnight(day), s_calendar, out tile, out CropDefinition? second, outdoors: false).Should().Be(FarmVerdict.Ok, "and can be harvested a second time");
        second!.Id.Should().Be(CropCatalog.GreenBean);
        tile.CropId.Should().Be(CropCatalog.GreenBean);
    }

    [Fact]
    public void Harvest_RegrowingCropWateredTheSameDay_CountsThatWateringForTheRegrowth()
    {
        // Indoors, so rain never waters the plant: only the hand watering below counts (docs/adr/ADR-027 D2).
        CropDefinition tomato = CropCatalog.All.Single(c => c.Id == CropCatalog.Tomato);
        long summer = s_calendar.EpochMs + s_calendar.SeasonLengthMs;
        int day = s_calendar.DayNumberAt(summer) + 1;
        FarmTile ripe = new(CropCatalog.Tomato, summer, tomato.TotalGrowthDays, day, 0);

        FarmRules.Harvest(ripe, Noon(day), s_calendar, out FarmTile next, out _, outdoors: false).Should().Be(FarmVerdict.Ok);

        next.WateredDay.Should().Be(day, "today's water is not lost");
        FarmRules.GrowthDaysAt(next, Midnight(day + 1), s_calendar, outdoors: false).Should().Be(tomato.GrowthDaysAfterHarvest + 1);
    }

    [Fact]
    public void Harvest_SingleHarvestCrop_LeavesBareSoilAsBefore()
    {
        FarmTile ripe = new(CropCatalog.Parsnip, s_calendar.EpochMs, CropCatalog.All[0].TotalGrowthDays, 0, 0);

        FarmRules.Harvest(ripe, Noon(10), s_calendar, out FarmTile next, out _).Should().Be(FarmVerdict.Ok);

        next.HasCrop.Should().BeFalse();
        next.TendedDay.Should().Be(10);
    }

    [Fact]
    public void Harvest_RegrowingCropOutOfItsSeason_IsWithered()
    {
        long summer = s_calendar.EpochMs + s_calendar.SeasonLengthMs;
        long fall = s_calendar.EpochMs + (2 * s_calendar.SeasonLengthMs);
        FarmTile regrown = new(CropCatalog.Tomato, summer + 1000, CropCatalog.All.Single(c => c.Id == CropCatalog.Tomato).TotalGrowthDays, 0, 0);

        FarmRules.Harvest(regrown, fall, s_calendar, out _, out _).Should().Be(FarmVerdict.Withered, "tomatoes do not survive into fall");
    }

    [Fact]
    public void Shop_SeedsOfOneSeason_AreOnlyTheOnesWhoseCropGrowsThen()
    {
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop);
        foreach (Season season in System.Enum.GetValues<Season>())
        {
            long now = s_calendar.EpochMs + ((int)season * s_calendar.SeasonLengthMs) + 1000;
            List<int> sellable = [.. shop!.Offers.Where(o => ShopRules.IsInSeason(o.ItemId, s_calendar, now)).Select(o => o.ItemId)];

            sellable.Should().NotBeEmpty($"the seed shop sells something in {season}");
            sellable.Should().OnlyContain(id => CropCatalog.All.Single(c => c.Id == ItemCatalog.All.Single(i => i.Id == id).CropId).GrowsIn(season));
            sellable.Should().HaveCount(CropCatalog.All.Count(c => c.GrowsIn(season)), "every crop of the season has its seed on offer");
        }
    }

    [Fact]
    public void ShippingPrices_BuyEveryProduceItemAndForage_AndNothingElse()
    {
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            ShippingPrices.TryGet(item.Id, out int coins).Should().Be(item.Kind == ItemKind.Produce, item.Key);
            (coins > 0).Should().Be(item.Kind == ItemKind.Produce, item.Key);
        }
    }
}
