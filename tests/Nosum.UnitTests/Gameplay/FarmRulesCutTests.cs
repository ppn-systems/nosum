// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Cutting a withered crop with a scythe clears the plant and keeps the tilled soil (docs/adr/ADR-018).</summary>
public sealed class FarmRulesCutTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    // A parsnip planted on tilled soil in spring: it withers once summer begins.
    private static FarmTile PlantedParsnip()
    {
        FarmRules.Till(true, null, Noon(1), s_calendar, out FarmTile tilled).Should().Be(FarmVerdict.Ok);
        FarmRules.Plant(tilled, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile planted).Should().Be(FarmVerdict.Ok);
        return planted;
    }

    [Fact]
    public void CutWithered_AWitheredCrop_LeavesTheTilledSoilBare()
    {
        FarmTile planted = PlantedParsnip();
        FarmRules.IsWithered(planted, Midnight(40), s_calendar).Should().BeTrue("the crop was planted in spring and summer has begun");

        FarmVerdict verdict = FarmRules.CutWithered(planted, Midnight(40), s_calendar, out FarmTile next);

        verdict.Should().Be(FarmVerdict.Ok);
        next.HasCrop.Should().BeFalse("the dead plant is gone");
        next.TendedDay.Should().Be(40, "cutting the crop tends the soil that day, as a harvest does");
    }

    [Fact]
    public void CutWithered_ALiveCrop_IsRefused()
    {
        FarmTile planted = PlantedParsnip();

        FarmRules.CutWithered(planted, Noon(2), s_calendar, out _).Should().Be(FarmVerdict.NothingToDo);
    }

    [Fact]
    public void CutWithered_BareSoil_IsRefused()
    {
        FarmRules.CutWithered(FarmTile.Bare, Midnight(40), s_calendar, out _).Should().Be(FarmVerdict.NothingToDo);
    }

    [Fact]
    public void CutWithered_UntilledGround_IsNotTilled()
    {
        FarmRules.CutWithered(null, Midnight(40), s_calendar, out _).Should().Be(FarmVerdict.NotTilled);
    }
}
