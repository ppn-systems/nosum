// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class FarmRulesDaysLeftTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    // Noon of a game day (day 1 is the first day since the epoch).
    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    [Fact]
    public void DaysUntilMature_NoCrop_IsZero()
        => FarmRules.DaysUntilMature(FarmTile.Bare, Noon(1), s_calendar, outdoors: false).Should().Be(0);

    [Fact]
    public void DaysUntilMature_JustPlanted_IsTheWholeGrowth()
    {
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile planted, outdoors: false).Should().Be(FarmVerdict.Ok);

        FarmRules.DaysUntilMature(planted, Noon(1), s_calendar, outdoors: false).Should().Be(CropCatalog.All[0].TotalGrowthDays);
    }

    [Fact]
    public void DaysUntilMature_CountsDownOneWateredDayAtATime_AndIsZeroWhenRipe()
    {
        int total = CropCatalog.All[0].TotalGrowthDays;
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile tile, outdoors: false).Should().Be(FarmVerdict.Ok);

        for (int day = 1; day <= total; day++)
        {
            FarmRules.Water(tile, Noon(day), s_calendar, out FarmTile watered, outdoors: false).Should().Be(FarmVerdict.Ok);
            tile = watered;

            // The day of watering has ended by the next midnight, so it counts then.
            int left = FarmRules.DaysUntilMature(tile, Midnight(day + 1), s_calendar, outdoors: false);
            left.Should().Be(Math.Max(0, total - day));
        }

        FarmRules.IsMature(tile, Midnight(total + 1), s_calendar, outdoors: false).Should().BeTrue();
    }
}
