// Copyright (c) 2026 PPN Systems. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>
/// Locks the economy balance targets of docs/design/economy.md (docs/adr/ADR-025, ADR-028 and ADR-078 amendments). The tests read the
/// live catalogs, so a data change that moves a crop, animal or fish out of its band fails the build rather than the economy.
/// </summary>
public sealed class EconomyBalanceTests
{
    /// <summary>Lower bound of the season profit per plot, coins: one crop tends one plot for one season (28 game days).</summary>
    private const long MinSeasonProfitPerPlot = 150;

    /// <summary>Upper bound of the season profit per plot, coins.</summary>
    private const long MaxSeasonProfitPerPlot = 260;

    /// <summary>Lowest coins one energy point earns from a crop or a fishing cast.</summary>
    private const double MinCoinsPerEnergy = 2.0;

    /// <summary>Highest coins one energy point earns from a crop or a fishing cast.</summary>
    private const double MaxCoinsPerEnergy = 3.2;

    /// <summary>Lowest share of an animal's price its everyday product pays back in one season.</summary>
    private const double MinAnimalSeasonShare = 0.40;

    /// <summary>Highest share of an animal's price its everyday product pays back in one season.</summary>
    private const double MaxAnimalSeasonShare = 0.60;

    /// <summary>Energy a player spends tending one plot for a day: watering it by hand.</summary>
    private static int WaterCost => EnergyRules.BaseCost(ToolKind.WateringCan);

    [Fact]
    public void SeasonProfitPerPlot_EveryCrop_IsWithinTheBand()
    {
        foreach (CropDefinition crop in CropCatalog.All)
        {
            (long profit, int _) = Season(crop);

            profit.Should().BeInRange(MinSeasonProfitPerPlot, MaxSeasonProfitPerPlot, crop.Key);
        }
    }

    [Fact]
    public void ProfitPerEnergy_EveryCrop_IsWithinTheBand()
    {
        foreach (CropDefinition crop in CropCatalog.All)
        {
            (long profit, int waterDays) = Season(crop);
            double energy = EnergyRules.BaseCost(ToolKind.Hoe) + (WaterCost * waterDays);

            (profit / energy).Should().BeInRange(MinCoinsPerEnergy, MaxCoinsPerEnergy, crop.Key);
        }
    }

    [Fact]
    public void EnergyPerDay_CoversSixtyToolUses_AtTheBasicTier()
    {
        (EnergyRules.MaxEnergy / EnergyRules.BaseCost(ToolKind.Hoe)).Should().BeGreaterThanOrEqualTo(60);
        (EnergyRules.MaxEnergy / EnergyRules.BaseCost(ToolKind.WateringCan)).Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void EnergyPerDay_WateringAFortyPlotFarm_UsesAtMostHalfTheDay()
    {
        (40 * WaterCost).Should().BeLessThanOrEqualTo(EnergyRules.MaxEnergy / 2);
    }

    [Fact]
    public void FishingCast_ExpectedCoinsPerEnergy_IsWithinTheBand()
    {
        long weighted = FishCatalog.All.Sum(species => (long)species.Weight * ItemSellPrice(species.ItemId));
        long weight = FishCatalog.All.Sum(species => (long)species.Weight);
        double catchValue = (double)weighted / weight;
        double nothingShare = 1 - (FishingRules.BaseNothingPermille / 1000.0);
        double coinsPerCast = catchValue * nothingShare;

        (coinsPerCast / FishingRules.CastEnergy).Should().BeInRange(MinCoinsPerEnergy, MaxCoinsPerEnergy);
    }

    [Fact]
    public void Animal_EverydayProductOverASeason_PaysBackBetweenAFifthAndAHalfOfItsPrice()
    {
        foreach (AnimalDefinition animal in AnimalCatalog.All)
        {
            long productDays = animal.Schedule switch
            {
                ProduceSchedule.EveryThirdDay => GameCalendar.DaysPerSeason / 3,
                ProduceSchedule.SunnyDays => GameCalendar.DaysPerSeason / 2,
                _ => GameCalendar.DaysPerSeason,
            };
            long seasonValue = productDays * ItemSellPrice(animal.Product);

            ((double)seasonValue / animal.Coins).Should().BeInRange(MinAnimalSeasonShare, MaxAnimalSeasonShare, animal.Key);
        }
    }

    /// <summary>Profit of one plot over one season and the watered days it takes, replanting single-harvest crops and regrowing harvested ones.</summary>
    private static (long Profit, int WaterDays) Season(CropDefinition crop)
    {
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop);
        int seed = shop!.Offers.Single(o => ItemCatalog.TryGet(o.ItemId, out ItemDefinition? i) && i.CropId == crop.Id).CoinsEach;
        long value = ItemSellPrice(crop.HarvestItemId) * crop.HarvestQuantity;
        int days = GameCalendar.DaysPerSeason;

        if (!crop.Regrows)
        {
            long cycles = days / crop.TotalGrowthDays;
            return (cycles * (value - seed), (int)(cycles * crop.TotalGrowthDays));
        }

        List<int> harvests = [];
        for (int day = crop.TotalGrowthDays; day <= days; day += crop.RegrowDays)
        {
            harvests.Add(day);
        }

        return ((harvests.Count * value) - seed, harvests[^1]);
    }

    private static long ItemSellPrice(int itemId)
        => ShippingPrices.TryGetSellPrice(itemId, out int coins) ? coins : throw new InvalidOperationException($"item {itemId} has no sell price");
}
