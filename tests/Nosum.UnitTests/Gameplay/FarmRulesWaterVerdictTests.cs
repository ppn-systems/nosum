// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class FarmRulesWaterVerdictTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    // Noon of a game day (day 1 is the first day since the epoch).
    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    [Fact]
    public void WaterVerdict_NoTile_IsNotTilled()
        => FarmRules.WaterVerdict(null, Noon(1), s_calendar).Should().Be(FarmVerdict.NotTilled);

    [Fact]
    public void WaterVerdict_DrySoil_IsOk()
        => FarmRules.WaterVerdict(FarmTile.Bare, Noon(1), s_calendar, outdoors: false).Should().Be(FarmVerdict.Ok);

    [Fact]
    public void WaterVerdict_SoilWateredToday_IsAlreadyWet()
    {
        FarmRules.Water(FarmTile.Bare, Noon(1), s_calendar, out FarmTile watered).Should().Be(FarmVerdict.Ok);

        FarmRules.WaterVerdict(watered, Noon(1), s_calendar, outdoors: false).Should().Be(FarmVerdict.AlreadyWet);
    }

    [Fact]
    public void WaterVerdict_SoilWetFromASprinkler_IsAlreadyWet()
    {
        long placed = Midnight(1);
        FarmRules.IsWet(FarmTile.Bare, Noon(2), s_calendar, outdoors: false, sprinklerSinceMs: placed).Should().BeTrue("the sprinkler's setup");

        FarmRules.WaterVerdict(FarmTile.Bare, Noon(2), s_calendar, outdoors: false, sprinklerSinceMs: placed).Should().Be(FarmVerdict.AlreadyWet);
    }

    [Fact]
    public void WaterVerdict_WitheredCrop_IsWithered()
    {
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile planted, outdoors: false).Should().Be(FarmVerdict.Ok);
        long dead = Noon(1 + FarmRules.DryDaysToWither + 1);
        FarmRules.IsWithered(planted, dead, s_calendar, outdoors: false).Should().BeTrue("the crop went unwatered past its dry days");

        FarmRules.WaterVerdict(planted, dead, s_calendar, outdoors: false).Should().Be(FarmVerdict.Withered);
    }

    [Fact]
    public void WaterVerdict_LiveCropOnDrySoil_IsOk()
    {
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile planted, outdoors: false).Should().Be(FarmVerdict.Ok);

        FarmRules.WaterVerdict(planted, Noon(1), s_calendar, outdoors: false).Should().Be(FarmVerdict.Ok);
    }

    [Fact]
    public void WaterVerdict_Ok_MatchesWhatWaterApplies()
    {
        FarmVerdict verdict = FarmRules.WaterVerdict(FarmTile.Bare, Noon(1), s_calendar, outdoors: false);

        FarmRules.Water(FarmTile.Bare, Noon(1), s_calendar, out _, outdoors: false).Should().Be(verdict);
    }
}
