// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>A sprinkler waters the tiles beside it from the day after it was placed, indoors as well as outdoors (docs/adr/ADR-024 D4).</summary>
public sealed class FarmRulesSprinklerTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    // The slowest crop that grows in spring, so a few watered days never make it mature.
    private static readonly CropDefinition s_slowCrop = CropCatalog.All.Where(c => c.GrowsIn(Season.Spring)).OrderByDescending(c => c.TotalGrowthDays).First();

    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    private static FarmTile PlantedOn(int day)
    {
        FarmRules.Plant(FarmTile.Bare, s_slowCrop.Id, Noon(day), s_calendar, out FarmTile planted).Should().Be(FarmVerdict.Ok);
        return planted;
    }

    [Fact]
    public void GrowthDaysAt_ASprinklerPlacedAfterPlanting_WaterOnlyTheDaysAfterItWasPlaced()
    {
        FarmTile tile = PlantedOn(1);

        // Indoors, so the rain cannot count: the sprinkler placed on day 6 waters days 7, 8 and 9 only, not the five days before it stood there.
        FarmRules.GrowthDaysAt(tile, Midnight(10), s_calendar, outdoors: false, sprinklerSinceMs: Noon(6)).Should().Be(3);
    }

    [Fact]
    public void GrowthDaysAt_ASprinklerIndoors_WatersEveryDayAfterTheDayItWasPlaced()
    {
        FarmTile tile = PlantedOn(1);

        // Placed on day 1: day 1 does not count, days 2 and 3 do, once they have ended.
        FarmRules.GrowthDaysAt(tile, Midnight(4), s_calendar, outdoors: false, sprinklerSinceMs: Noon(1)).Should().Be(2);
    }

    [Fact]
    public void IsWateredOn_ASprinklerOnTheDayItIsPlaced_DoesNotWaterThatDay()
    {
        FarmTile tile = PlantedOn(1);

        FarmRules.IsWateredOn(tile, 1, s_calendar, outdoors: false, sprinklerSinceMs: Noon(1)).Should().BeFalse();
        FarmRules.IsWateredOn(tile, 2, s_calendar, outdoors: false, sprinklerSinceMs: Noon(1)).Should().BeTrue();
    }

    [Fact]
    public void IsReverted_BareSoilIndoorsBesideASprinkler_NeverDriesOut()
    {
        FarmTile bare = FarmTile.Bare with { TendedDay = 1 };

        // Without the sprinkler the same soil dries out and reverts; with it indoors, every day is watered.
        FarmRules.IsReverted(bare, Midnight(30), s_calendar, outdoors: false).Should().BeTrue();
        FarmRules.IsReverted(bare, Midnight(30), s_calendar, outdoors: false, sprinklerSinceMs: Noon(1)).Should().BeFalse();
    }

    [Fact]
    public void WateredSinceMs_TheEarliestSprinklerThatWatersTheTile_Decides()
    {
        PlacedItem[] placed =
        [
            new(1, ItemCatalog.Sprinkler, 10, 10, 0, 500),
            new(2, ItemCatalog.Sprinkler, 12, 10, 0, 300),
            new(3, ItemCatalog.Chest, 11, 9, 0, 100),
        ];

        SprinklerRules.WateredSinceMs(placed, 11, 10).Should().Be(300, "both sprinklers water the tile between them; the earlier one has watered it longest");
        SprinklerRules.WateredSinceMs(placed, 10, 9).Should().Be(500);
        SprinklerRules.WateredSinceMs(placed, 3, 4).Should().BeNull("no sprinkler stands beside it, and a chest does not water");
    }
}
