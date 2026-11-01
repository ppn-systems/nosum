// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>A sprinkler beside a crop waters it, so the growth stage the mouse names counts those days (docs/adr/ADR-106 D2).</summary>
public sealed class FarmStageSprinklerTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    [Fact]
    public void StageAt_ASprinklerBesideTheCrop_AdvancesTheStageLikeWatering()
    {
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile planted, outdoors: false).Should().Be(FarmVerdict.Ok);

        // The sprinkler is placed on day 1 and waters the crop from day 2; the day after the second watered day has ended, the crop has grown.
        long placed = Noon(1);
        int withSprinkler = FarmRules.StageAt(planted, Midnight(4), s_calendar, outdoors: false, sprinklerSinceMs: placed);
        int withoutSprinkler = FarmRules.StageAt(planted, Midnight(4), s_calendar, outdoors: false, sprinklerSinceMs: null);

        withSprinkler.Should().BeGreaterThan(withoutSprinkler);
    }

    [Fact]
    public void Waters_AFourNeighbourSprinkler_WatersOnlyItsOrthogonalNeighbours()
    {
        // The rule the client now reads for the hover stage is the one the server reads for growth (SprinklerRules.Waters).
        SprinklerRules.Waters(10, 10, 11, 10).Should().BeTrue();
        SprinklerRules.Waters(10, 10, 10, 10).Should().BeFalse();
        SprinklerRules.Waters(10, 10, 11, 11).Should().BeFalse();
    }
}
