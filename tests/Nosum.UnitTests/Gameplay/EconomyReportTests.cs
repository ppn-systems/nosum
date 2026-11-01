// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Pets;
using Nosum.Gameplay.Shops;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>
/// The economy simulation of docs/design/critique-economy.md. It reads the live catalogs and works out what one player earns and spends in a
/// season: crops per plot and per farm, fishing per cast and per energy point, animals per day and their feed. The report is written to the path in
/// <c>NOSUM_ECONOMY_REPORT</c> when that is set (a tester's run); the test itself asserts only that the report is complete, the balance bands are
/// locked by <see cref="EconomyBalanceTests"/>.
/// </summary>
public sealed class EconomyReportTests
{
    /// <summary>Plots of the reference farm: the forty-plot farm the balance targets assume (docs/adr/ADR-081 D3).</summary>
    private const int ReferencePlots = 40;

    [Fact]
    public void Report_CoversCropsFishingAnimalsAndSinks()
    {
        string report = Build();

        report.Should().Contain("== CROPS");
        report.Should().Contain("== FISHING");
        report.Should().Contain("== ANIMALS");
        report.Should().Contain("== SINKS");
        report.Should().Contain("reference farm");
        string? path = Environment.GetEnvironmentVariable("NOSUM_ECONOMY_REPORT");
        if (path is not null)
        {
            File.WriteAllText(path, report);
        }
    }

    private static string Build()
    {
        StringBuilder sb = new();
        int days = GameCalendar.DaysPerSeason;

        sb.AppendLine("== CROPS (one plot, a 28-day season, replanted, watered daily)");
        sb.AppendLine("crop\tseed\tbin_each\tyield\tharvests\tprofit\tseed_share_of_harvest\tcoins_per_energy\tenergy\tcoins_per_day");
        List<long> profits = [];
        foreach (CropDefinition crop in CropCatalog.All)
        {
            int seed = SeedCost(crop);
            long value = Sell(crop.HarvestItemId) * crop.HarvestQuantity;
            (long profit, int harvests, int waterDays) = Season(crop, seed, value, days);
            double energy = EnergyRules.BaseCost(ToolKind.Hoe) + (EnergyRules.BaseCost(ToolKind.WateringCan) * waterDays);
            profits.Add(profit);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{crop.Key}\t{seed}\t{Sell(crop.HarvestItemId)}\t{crop.HarvestQuantity}\t{harvests}\t{profit}\t{(double)seed / Math.Max(1, value):P0}\t{profit / energy:F2}\t{energy}\t{profit / (double)days:F1}"));
        }

        double cropAverage = profits.Average();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"crop profit per plot-season: min {profits.Min()}, average {cropAverage:F0}, max {profits.Max()}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"reference farm ({ReferencePlots} plots): crops {ReferencePlots * cropAverage / days:F1} coins a game day"));

        sb.AppendLine("== FISHING (expected value per cast)");
        long weighted = FishCatalog.All.Sum(s => (long)s.Weight * Sell(s.ItemId));
        long weight = FishCatalog.All.Sum(s => (long)s.Weight);
        double perCast = ((double)weighted / weight) * (1 - (FishingRules.BaseNothingPermille / 1000.0));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"expected {perCast:F1} coins a cast for {FishingRules.CastEnergy} energy: {perCast / FishingRules.CastEnergy:F2} coins per energy"));
        int spare = EnergyRules.MaxEnergy - (ReferencePlots * EnergyRules.BaseCost(ToolKind.WateringCan));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"reference farm: {spare} spare energy a game day (after watering {ReferencePlots} plots) fishes {spare / FishingRules.CastEnergy} casts, {(spare / FishingRules.CastEnergy) * perCast:F1} coins a game day"));

        sb.AppendLine("== ANIMALS (one of each; feed is one ration a night)");
        sb.AppendLine("animal\tprice\tproduct\tseason_product_value\tpayback_seasons\tfed_with_hay_net_per_season\tfed_payback_seasons");
        long hay = ShopPriceOf(ShopCatalog.CarpenterShop, ItemCatalog.Hay);
        long herdPrice = 0;
        double herdPerDay = 0;
        foreach (AnimalDefinition animal in AnimalCatalog.All)
        {
            double perDay = animal.Schedule switch
            {
                ProduceSchedule.EveryThirdDay => 1.0 / 3,
                ProduceSchedule.SunnyDays => 0.5,
                _ => 1.0,
            };
            long season = (long)(perDay * days) * Sell(animal.Product);
            herdPrice += animal.Coins;
            herdPerDay += perDay * Sell(animal.Product);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{animal.Key}\t{animal.Coins}\t{Sell(animal.Product)}\t{season}\t{(double)animal.Coins / season:F2}\t{season - (hay * days)}\t{(double)animal.Coins / (season - (hay * days)):F2}"));
        }

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"herd: {herdPrice} coins of animals, {herdPerDay:F1} coins a game day, payback {herdPrice / herdPerDay:F0} game days; no energy"));

        sb.AppendLine("== SINKS (coins out, one purchase)");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"hay {hay} (one ration: one animal, one night)"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"milk pail {ShopPriceOf(ShopCatalog.CarpenterShop, ItemCatalog.MilkPail)}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"backpack: {BackpackRules.TryNext(36, out BackpackQuote first) && first.Capacity == 45} first upgrade {first.Coins}; next {(BackpackRules.TryNext(45, out BackpackQuote second) ? second.Coins : 0)}"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"copper tool upgrade {BlacksmithRules.CopperCoins} plus bars; iron {BlacksmithRules.IronCoins} plus bars"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cat {PetRules.CatPrice}; dog {PetRules.DogPrice}; rehome refund {PetRules.RehomeRefundPercent} %"));
        return sb.ToString();
    }

    private static (long Profit, int Harvests, int WaterDays) Season(CropDefinition crop, int seed, long value, int days)
    {
        if (!crop.Regrows)
        {
            long cycles = days / crop.TotalGrowthDays;
            return (cycles * (value - seed), (int)cycles, (int)(cycles * crop.TotalGrowthDays));
        }

        List<int> harvests = [];
        for (int day = crop.TotalGrowthDays; day <= days; day += crop.RegrowDays)
        {
            harvests.Add(day);
        }

        return ((harvests.Count * value) - seed, harvests.Count, harvests[^1]);
    }

    private static int SeedCost(CropDefinition crop)
    {
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop);
        return shop!.Offers.Single(o => ItemCatalog.TryGet(o.ItemId, out ItemDefinition? i) && i.CropId == crop.Id).CoinsEach;
    }

    private static long ShopPriceOf(int shopId, int itemId)
    {
        ShopCatalog.TryGet(shopId, out ShopDefinition? shop);
        return shop!.TryGetPrice(itemId, out int coins) ? coins : throw new InvalidOperationException($"item {itemId} is not sold at shop {shopId}");
    }

    private static long Sell(int itemId)
        => ShippingPrices.TryGetSellPrice(itemId, out int coins) ? coins : throw new InvalidOperationException($"item {itemId} has no sell price");
}
