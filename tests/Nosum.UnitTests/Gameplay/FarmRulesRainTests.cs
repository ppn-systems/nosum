// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>A rainy day counts as watered for outdoor tilled soil, without any write; indoors it does not (docs/adr/ADR-027 D2).</summary>
public sealed class FarmRulesRainTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;
    private static readonly CropDefinition s_slowCrop = CropCatalog.All.Where(c => c.GrowsIn(Season.Spring)).OrderByDescending(c => c.TotalGrowthDays).First();

    // A sprinkler standing since the first game day: it waters every day from the second on, which is every day these tests look at.
    private static readonly long s_sprinklerSince = s_calendar.DayStartMs(1);

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    private static bool Rains(int day) => WeatherRules.WatersOn(s_calendar, day);

    private static int FirstDay(Func<int, bool> match, int from = 3) => Enumerable.Range(from, 400).First(match);

    private static FarmTile PlantedOn(int day, int cropId = CropCatalog.Parsnip)
    {
        FarmRules.Plant(FarmTile.Bare, cropId, Noon(day), s_calendar, out FarmTile planted).Should().Be(FarmVerdict.Ok);
        return planted;
    }

    [Fact]
    public void GrowthDaysAt_AnUnwateredCropOutdoors_GrowsOneDayForEachRainyDayThatEnded()
    {
        int rainy = FirstDay(Rains);
        FarmTile tile = PlantedOn(rainy);

        FarmRules.GrowthDaysAt(tile, Noon(rainy), s_calendar).Should().Be(0, "the rainy day has not ended");
        FarmRules.GrowthDaysAt(tile, Midnight(rainy + 1), s_calendar).Should().Be(1);
    }

    [Fact]
    public void GrowthDaysAt_AnUnwateredCropIndoors_IgnoresTheRain()
    {
        int rainy = FirstDay(Rains);
        FarmTile tile = PlantedOn(rainy);

        FarmRules.GrowthDaysAt(tile, Midnight(rainy + 1), s_calendar, outdoors: false).Should().Be(0);
        FarmRules.IsWet(tile, Noon(rainy), s_calendar, outdoors: false).Should().BeFalse();
    }

    [Fact]
    public void GrowthDaysAt_RainAndWateringCanOnTheSameDay_CountOnce()
    {
        int rainy = FirstDay(Rains);
        FarmTile tile = PlantedOn(rainy);
        FarmRules.Water(tile, Noon(rainy), s_calendar, out FarmTile watered).Should().Be(FarmVerdict.Ok);

        FarmRules.GrowthDaysAt(watered, Midnight(rainy + 1), s_calendar).Should().Be(1);
    }

    [Fact]
    public void GrowthDaysAt_ADryDay_AddsNothing()
    {
        int dry = FirstDay(day => !Rains(day) && !Rains(day + 1));
        FarmTile tile = PlantedOn(dry);

        FarmRules.GrowthDaysAt(tile, Midnight(dry + 1), s_calendar).Should().Be(0);
        FarmRules.IsWet(tile, Noon(dry), s_calendar).Should().BeFalse();
    }

    [Fact]
    public void IsWet_OnARainyDay_IsTrueOutdoorsWithoutWatering()
    {
        int rainy = FirstDay(Rains);

        FarmRules.IsWet(FarmTile.Bare, Noon(rainy), s_calendar).Should().BeTrue();
        FarmRules.IsWet(FarmTile.Bare, Noon(rainy), s_calendar, outdoors: false).Should().BeFalse();
    }


    // The model: a day is watered when it rained (outdoors) or the can was used on it. The rules, with watering settling the days before it,
    // must agree with a plain count for any pattern of watering.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void GrowthDaysAt_AnyWateringPattern_EqualsRainyOrWateredDayCount(int stride)
    {
        const int plantDay = 4;
        FarmTile tile = PlantedOn(plantDay, s_slowCrop.Id);
        HashSet<int> watered = [];

        // Stays inside the spring the crop grows in: a withered crop refuses water (ADR-021 D7, amended).
        for (int day = plantDay; day < plantDay + 24; day++)
        {
            // Watered at least every other day, so no run of two dry days withers the crop (ADR-021 D7, amended).
            if ((day * stride) % 2 == 0)
            {
                FarmRules.Water(tile, Noon(day), s_calendar, out tile).Should().Be(FarmVerdict.Ok);
                watered.Add(day);
            }

            int ended = Enumerable.Range(plantDay, day - plantDay).Count(d => Rains(d) || watered.Contains(d));
            FarmRules.GrowthDaysAt(tile, Midnight(day), s_calendar).Should().Be(Math.Min(s_slowCrop.TotalGrowthDays, ended), $"day {day} stride {stride}");
        }
    }

    [Fact]
    public void GrowthDaysAt_AMatureCropOutdoors_IsCappedAndCheapToEvaluateAfterALongGap()
    {
        FarmTile tile = PlantedOn(3);

        FarmRules.GrowthDaysAt(tile, Midnight(10_000), s_calendar).Should().Be(CropCatalog.All[0].TotalGrowthDays);
    }

    [Fact]
    public void IsWateredOn_IsTheSingleSourceOfRainWater()
    {
        int rainy = FirstDay(Rains);
        int dry = FirstDay(d => !Rains(d));

        FarmRules.IsWateredOn(FarmTile.Bare, rainy, s_calendar).Should().BeTrue();
        FarmRules.IsWateredOn(FarmTile.Bare, dry, s_calendar).Should().BeFalse();
        FarmRules.IsWateredOn(FarmTile.Bare, rainy, s_calendar, outdoors: false).Should().BeFalse();
    }

    [Fact]
    public void MapCatalogue_OnlyTheFarmhouseTheMineAndBuildingInteriorsAreIndoors_AndNoFarmableMapIs()
    {
        // The mine's cave and its floors are caves (docs/adr/ADR-026, ADR-027): no weather, no rain watering.
        GameMaps.All.Where(m => m.Indoors).Select(m => m.Id).Should().BeEquivalentTo([GameMaps.FarmHouse, GameMaps.Coop, GameMaps.Barn, GameMaps.Mine, .. GameMaps.All.Where(m => m.IsMineFloor).Select(m => m.Id)]);
        GameMaps.All.Where(m => m.IsMineFloor || m.IsInterior).Should().OnlyContain(m => m.Indoors);
        GameMaps.All.Where(m => m.Farmable).Should().OnlyContain(m => !m.Indoors);
        MapInstance.Of(GameMaps.TryGet(GameMaps.FarmHouse, out MapSpec? house) ? house : throw new InvalidOperationException(), Guid.NewGuid()).IsOutdoors.Should().BeFalse();
        MapInstance.Shared(GameMaps.Farm).IsOutdoors.Should().BeTrue();
    }

    [Fact]
    public void FloorLamp_GivesLight_AndOtherFurnitureDoesNot()
    {
        PlaceableCatalog.TryGet(Nosum.Gameplay.Items.ItemCatalog.FloorLamp, out PlaceableDefinition? lamp).Should().BeTrue();
        lamp!.LightRadius.Should().Be(PlaceableCatalog.FloorLampLightRadius).And.BePositive();
        PlaceableCatalog.All.Where(p => p.LightRadius > 0).Select(p => p.ItemId).Should().BeEquivalentTo(
            new[]
            {
                Nosum.Gameplay.Items.ItemCatalog.FloorLamp,
                Nosum.Gameplay.Items.ItemCatalog.Candelabra,
                Nosum.Gameplay.Items.ItemCatalog.TableLamp,
                Nosum.Gameplay.Items.ItemCatalog.WallSconce,
                Nosum.Gameplay.Items.ItemCatalog.PaperLantern,
            },
            "the lamps of the decorating update give night light (docs/adr/ADR-073 D4)");
    }

    [Fact]
    public void GrowthDaysAt_SprinkledOutdoors_GrowsEveryDayWithoutHandOrRain()
    {
        int dry = FirstDay(d => Enumerable.Range(d, 6).All(x => !Rains(x)));
        FarmTile tile = PlantedOn(dry);

        FarmRules.GrowthDaysAt(tile, Midnight(dry + 3), s_calendar, outdoors: true, sprinklerSinceMs: s_sprinklerSince).Should().Be(3);
        FarmRules.GrowthDaysAt(tile, Midnight(dry + 3), s_calendar, outdoors: true, sprinklerSinceMs: null).Should().Be(0, "an uncovered, unwatered crop does not grow on dry days");
        FarmRules.IsMature(tile, Midnight(dry + CropCatalog.All[0].TotalGrowthDays), s_calendar, outdoors: true, sprinklerSinceMs: s_sprinklerSince).Should().BeTrue();
        FarmRules.IsMature(tile, Midnight(dry + CropCatalog.All[0].TotalGrowthDays), s_calendar, outdoors: true, sprinklerSinceMs: null).Should().BeFalse();
        FarmRules.Harvest(tile, Midnight(dry + CropCatalog.All[0].TotalGrowthDays), s_calendar, out _, out _, outdoors: true, sprinklerSinceMs: s_sprinklerSince).Should().Be(FarmVerdict.Ok, "the harvest sees the same water as the growth");
        FarmRules.Harvest(tile, Midnight(dry + CropCatalog.All[0].TotalGrowthDays), s_calendar, out _, out _, outdoors: true, sprinklerSinceMs: null).Should().Be(FarmVerdict.Withered, "without the sprinkler the crop went two dry days and withered (ADR-021 D7, amended)");
    }

    [Fact]
    public void GrowthDaysAt_SprinkledDay_CountsOnceWithRainAndHandWater()
    {
        int rainy = FirstDay(Rains);
        FarmTile tile = PlantedOn(rainy);
        FarmRules.Water(tile, Noon(rainy), s_calendar, out FarmTile watered, outdoors: true, sprinklerSinceMs: s_sprinklerSince).Should().Be(FarmVerdict.Ok);

        FarmRules.GrowthDaysAt(watered, Midnight(rainy + 1), s_calendar, outdoors: true, sprinklerSinceMs: s_sprinklerSince).Should().Be(1);
    }

    [Fact]
    public void GrowthDaysAt_SprinkledIndoors_IsWatered()
    {
        int dry = FirstDay(d => !Rains(d) && !Rains(d + 1));
        FarmTile tile = PlantedOn(dry);

        FarmRules.GrowthDaysAt(tile, Midnight(dry + 1), s_calendar, outdoors: false, sprinklerSinceMs: s_sprinklerSince).Should().Be(1, "a sprinkler waters indoors too (docs/adr/ADR-024 D4)");
    }

    [Fact]
    public void IsWet_NextToASprinkler_IsWetOnADryDay()
    {
        int dry = FirstDay(d => !Rains(d));

        FarmRules.IsWet(FarmTile.Bare, Noon(dry), s_calendar, outdoors: true, sprinklerSinceMs: s_sprinklerSince).Should().BeTrue();
        FarmRules.IsWet(FarmTile.Bare, Noon(dry), s_calendar, outdoors: true, sprinklerSinceMs: null).Should().BeFalse();
    }


    [Fact]
    public void Harvest_RegrowingCropOutdoors_DoesNotCountTheRainBeforeTheHarvestAgain()
    {
        CropDefinition berry = CropCatalog.All.Where(c => c.Regrows && c.GrowsIn(Season.Spring)).OrderBy(c => c.TotalGrowthDays).First();
        int plant = FirstDay(d => ((d - 1) % GameCalendar.DaysPerSeason) + berry.TotalGrowthDays + berry.RegrowDays + 2 <= GameCalendar.DaysPerSeason
            && (Season)(((d - 1) / GameCalendar.DaysPerSeason) % GameCalendar.SeasonsPerYear) == Season.Spring
            && Enumerable.Range(d, berry.TotalGrowthDays).Any(Rains));
        FarmTile tile = PlantedOn(plant, berry.Id);
        int day = plant;
        for (; day < plant + berry.TotalGrowthDays; day++)
        {
            FarmRules.Water(tile, Noon(day), s_calendar, out tile).Should().Be(FarmVerdict.Ok);
        }

        FarmRules.Harvest(tile, Midnight(day), s_calendar, out FarmTile regrowing, out _).Should().Be(FarmVerdict.Ok);

        FarmRules.GrowthDaysAt(regrowing, Midnight(day), s_calendar).Should().Be(berry.GrowthDaysAfterHarvest, "the rainy days before the harvest are spent");
    }
}
