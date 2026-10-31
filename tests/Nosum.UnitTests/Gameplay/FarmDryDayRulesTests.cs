// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

// The dry-day rules of docs/adr/ADR-021 D7 (amended 2026-10-31): bare soil reverts after three dry game days in a row, a crop withers after two, and
// a watered day (by hand, by rain outdoors or by a sprinkler) restarts both runs.
public sealed class FarmDryDayRulesTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    private static bool Rains(int day) => WeatherRules.WatersOn(s_calendar, day);

    private static long SprinklerSince => s_calendar.DayStartMs(1);

    private static FarmTile Planted(int day) => new(CropCatalog.Parsnip, Noon(day), 0, 0, 0);

    private static FarmTile WateredOn(FarmTile tile, int day)
    {
        _ = FarmRules.Water(tile, Noon(day), s_calendar, out FarmTile watered, outdoors: false);
        return watered;
    }

    [Fact]
    public void DryDaysConstants_AreThreeToRevertAndTwoToWither()
    {
        FarmRules.DryDaysToRevert.Should().Be(3, "the soil reverts after three dry days in a row (ADR-021 D7, amended)");
        FarmRules.DryDaysToWither.Should().Be(2, "a crop withers after two dry days in a row (ADR-021 D7, amended)");
    }

    [Fact]
    public void IsReverted_BareDrySoil_RevertsAfterThreeDryDaysAndNotBefore()
    {
        FarmTile tilled = FarmTile.Bare with { TendedDay = 1 };

        FarmRules.IsReverted(tilled, Midnight(2), s_calendar, outdoors: false).Should().BeFalse("the tilling day is a day of grace; day 1 has ended");
        FarmRules.IsReverted(tilled, Midnight(4), s_calendar, outdoors: false).Should().BeFalse("only days 2 and 3 ended dry");
        FarmRules.IsReverted(tilled, Midnight(5), s_calendar, outdoors: false).Should().BeTrue("days 2, 3 and 4 ended dry");
        FarmRules.IsReverted(tilled, Noon(50), s_calendar, outdoors: false).Should().BeTrue("a reverted tile stays reverted");
    }

    [Fact]
    public void IsReverted_SameQuestionAskedTwice_GivesTheSameAnswer()
    {
        FarmTile tilled = FarmTile.Bare with { TendedDay = 2 };

        bool first = FarmRules.IsReverted(tilled, Noon(40), s_calendar);

        FarmRules.IsReverted(tilled, Noon(40), s_calendar).Should().Be(first);
    }

    [Fact]
    public void IsReverted_HandWateringOnDayTwo_RestartsTheDryRun()
    {
        FarmTile watered = WateredOn(FarmTile.Bare with { TendedDay = 1 }, 2);

        FarmRules.IsReverted(watered, Midnight(5), s_calendar, outdoors: false).Should().BeFalse("only days 3 and 4 ended dry after the watering");
        FarmRules.IsReverted(watered, Midnight(6), s_calendar, outdoors: false).Should().BeTrue("days 3, 4 and 5 ended dry");
    }

    [Fact]
    public void IsReverted_OutdoorsRainBreaksTheDryRun_IndoorsItDoesNot()
    {
        // Rain on day rainy, dry days either side (checked below): indoors the run of days rainy-1..rainy+1 reverts the soil, outdoors the rain restarts it.
        int rainy = Enumerable.Range(3, 500).First(d => Rains(d) && !Rains(d - 1) && !Rains(d + 1) && !Rains(d + 2) && !Rains(d + 3) && !Rains(d + 4));
        FarmTile tilled = FarmTile.Bare with { TendedDay = rainy - 2 };

        FarmRules.IsReverted(tilled, Midnight(rainy + 2), s_calendar, outdoors: false).Should().BeTrue("days rainy-1, rainy and rainy+1 are dry indoors");
        FarmRules.IsReverted(tilled, Midnight(rainy + 2), s_calendar, outdoors: true).Should().BeFalse("the rain on the rainy day restarts the run");
        FarmRules.IsReverted(tilled, Midnight(rainy + 5), s_calendar, outdoors: true).Should().BeTrue("days rainy+1 to rainy+3 are dry");
    }

    [Fact]
    public void IsReverted_BareSoilBesideASprinklerOutdoors_NeverReverts()
    {
        FarmTile tilled = FarmTile.Bare with { TendedDay = 1 };

        FarmRules.IsReverted(tilled, Midnight(30), s_calendar, outdoors: true, sprinklerSinceMs: SprinklerSince).Should().BeFalse("the sprinkler waters every day after it was placed");
        FarmRules.IsReverted(tilled, Midnight(30), s_calendar, outdoors: false, sprinklerSinceMs: SprinklerSince).Should().BeFalse();
    }

    [Fact]
    public void IsReverted_SoilWithACrop_NeverReverts()
        => FarmRules.IsReverted(Planted(2), Noon(500), s_calendar, outdoors: false).Should().BeFalse();

    [Fact]
    public void IsReverted_AfterHarvest_StartsDryDaysFromTheHarvestDay()
    {
        // Watered on day 9, so the crop is not two dry days old at the harvest on day 10.
        FarmTile ready = new(CropCatalog.Parsnip, Noon(1), CropCatalog.All[0].TotalGrowthDays, 9, 0);
        FarmRules.Harvest(ready, Noon(10), s_calendar, out FarmTile bare, out _, outdoors: false).Should().Be(FarmVerdict.Ok);

        FarmRules.IsReverted(bare, Midnight(13), s_calendar, outdoors: false).Should().BeFalse("days 11 and 12 ended dry");
        FarmRules.IsReverted(bare, Midnight(14), s_calendar, outdoors: false).Should().BeTrue("days 11, 12 and 13 ended dry");
    }

    [Fact]
    public void IsWithered_TwoDryDaysAfterPlanting_WithersTheCrop()
    {
        FarmTile planted = Planted(2);

        FarmRules.IsWithered(planted, Midnight(4), s_calendar, outdoors: false).Should().BeFalse("only day 3 ended dry");
        FarmRules.IsWithered(planted, Midnight(5), s_calendar, outdoors: false).Should().BeTrue("days 3 and 4 ended without water");
    }

    [Fact]
    public void IsWithered_WateredInTheMiddleOfTheRun_DoesNotWither()
    {
        FarmTile planted = Planted(2);
        FarmTile watered = WateredOn(planted, 4);

        FarmRules.IsWithered(watered, Midnight(6), s_calendar, outdoors: false).Should().BeFalse("only day 5 ended dry after the watering on day 4");
        FarmRules.IsWithered(watered, Midnight(7), s_calendar, outdoors: false).Should().BeTrue("days 5 and 6 ended dry");
    }

    [Fact]
    public void IsWithered_SprinklerBesideTheCrop_KeepsItAlive()
    {
        FarmTile planted = Planted(2);

        FarmRules.IsWithered(planted, Midnight(10), s_calendar, outdoors: false).Should().BeTrue();
        FarmRules.IsWithered(planted, Midnight(10), s_calendar, outdoors: false, sprinklerSinceMs: SprinklerSince).Should().BeFalse();
    }

    [Fact]
    public void IsWithered_OnceWithered_StaysWitheredAfterRain()
    {
        FarmTile planted = Planted(2);
        FarmRules.IsWithered(planted, Midnight(5), s_calendar, outdoors: true).Should().BeTrue("the dry run of days 3 and 4 is a fact of the past");

        int wet = Enumerable.Range(5, 40).First(Rains);
        FarmRules.IsWithered(planted, Midnight(wet + 2), s_calendar, outdoors: true).Should().BeTrue("rain later does not revive a dead crop");
    }

    [Fact]
    public void Water_WitheredCrop_IsRefusedAsWithered()
    {
        FarmTile planted = Planted(2);

        FarmRules.Water(planted, Midnight(5), s_calendar, out FarmTile next, outdoors: false).Should().Be(FarmVerdict.Withered);
        next.Should().Be(default(FarmTile));
    }

    [Fact]
    public void Plant_OverAWitheredCrop_ReplacesIt()
    {
        FarmTile withered = Planted(2);

        FarmRules.Plant(withered, CropCatalog.Parsnip, Midnight(5), s_calendar, out FarmTile next, outdoors: false).Should().Be(FarmVerdict.Ok);
        next.CropId.Should().Be(CropCatalog.Parsnip);
    }

    [Fact]
    public void CutWithered_DeadCropAfterTwoDryDays_LeavesBareTilledSoil()
    {
        FarmTile withered = Planted(2);

        FarmRules.CutWithered(withered, Midnight(5), s_calendar, out FarmTile bare, outdoors: false).Should().Be(FarmVerdict.Ok);
        bare.HasCrop.Should().BeFalse();
        bare.TendedDay.Should().Be(5, "the cut counts as tending, so the dry days start again from there");
    }
}
