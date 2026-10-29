// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>A harvest on a rainy day, or the day after rain, outdoors may yield one bonus unit, decided by a deterministic roll (docs/adr/ADR-027 D8).</summary>
public sealed class FarmRulesHarvestBonusTests
{
    private const string Instance = "farm-instance-1";

    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static bool Rains(int day) => WeatherRules.WatersOn(s_calendar, day);

    private static int FirstDay(Func<int, bool> match) => Enumerable.Range(3, 400).First(match);

    [Fact]
    public void IsBonusHarvest_AnIndoorTileOnARainyDay_IsNeverBonus()
    {
        int rainy = FirstDay(Rains);

        for (int x = 0; x < 200; x++)
        {
            FarmRules.IsBonusHarvest(Instance, x, 4, Noon(rainy), s_calendar, outdoors: false).Should().BeFalse();
        }
    }

    [Fact]
    public void IsBonusHarvest_ADryDayAfterADryDay_IsNeverBonus()
    {
        int dry = FirstDay(day => !Rains(day) && !Rains(day - 1));

        for (int x = 0; x < 200; x++)
        {
            FarmRules.IsBonusHarvest(Instance, x, 4, Noon(dry), s_calendar, outdoors: true).Should().BeFalse();
        }
    }

    [Fact]
    public void IsBonusHarvest_TheDayAfterRain_CanBeBonus()
    {
        int after = FirstDay(day => !Rains(day) && Rains(day - 1));

        Enumerable.Range(0, 200).Any(x => FarmRules.IsBonusHarvest(Instance, x, 4, Noon(after), s_calendar, outdoors: true)).Should().BeTrue();
    }

    [Fact]
    public void IsBonusHarvest_ARainyDay_CanBeBonus()
    {
        int rainy = FirstDay(Rains);

        Enumerable.Range(0, 200).Any(x => FarmRules.IsBonusHarvest(Instance, x, 4, Noon(rainy), s_calendar, outdoors: true)).Should().BeTrue();
    }

    [Fact]
    public void IsBonusHarvest_ARainyDay_HitsAboutTheChancePercent()
    {
        int rainy = FirstDay(Rains);

        int hits = Enumerable.Range(0, 1000).Count(x => FarmRules.IsBonusHarvest(Instance, x, 7, Noon(rainy), s_calendar, outdoors: true));

        hits.Should().BeInRange(150, 250);
    }

    [Fact]
    public void IsBonusHarvest_TheSameTileAndDay_AlwaysAnswersTheSame()
    {
        int rainy = FirstDay(Rains);

        for (int x = 0; x < 50; x++)
        {
            bool first = FarmRules.IsBonusHarvest(Instance, x, 2, Noon(rainy), s_calendar, outdoors: true);
            FarmRules.IsBonusHarvest(Instance, x, 2, Noon(rainy), s_calendar, outdoors: true).Should().Be(first);
        }
    }
}
