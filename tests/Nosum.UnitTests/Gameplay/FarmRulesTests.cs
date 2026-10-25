// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class FarmRulesTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;
    private static readonly int s_parsnipDays = CropCatalog.All[0].TotalGrowthDays;

    // Noon of a game day (day 1 is the first day since the epoch).
    private static long Noon(int day) => s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 2);

    private static long Midnight(int day) => s_calendar.DayStartMs(day);

    private static FarmTile Planted(int cropId, int day)
    {
        FarmRules.Plant(FarmTile.Bare, cropId, Noon(day), s_calendar, out FarmTile planted).Should().Be(FarmVerdict.Ok);
        return planted;
    }

    private static FarmTile WateredOn(FarmTile tile, int day)
    {
        FarmRules.Water(tile, Noon(day), s_calendar, out FarmTile watered).Should().Be(FarmVerdict.Ok);
        return watered;
    }

    private static FarmTile PlantedParsnip(long plantedAt) => new(CropCatalog.Parsnip, plantedAt, 0, 0, 0);

    [Fact]
    public void Till_OnTillableUntilledTile_GivesBareSoilTendedToday()
    {
        FarmVerdict verdict = FarmRules.Till(true, null, Noon(3), s_calendar, out FarmTile next);

        verdict.Should().Be(FarmVerdict.Ok);
        next.Should().Be(FarmTile.Bare with { TendedDay = 3 });
    }

    [Fact]
    public void Till_OnGroundThatIsNotTillable_IsRefused()
        => FarmRules.Till(false, null, Noon(1), s_calendar, out _).Should().Be(FarmVerdict.NotTillable);

    [Fact]
    public void Till_OnTilledTile_IsRefused()
        => FarmRules.Till(true, FarmTile.Bare, Noon(1), s_calendar, out _).Should().Be(FarmVerdict.AlreadyTilled);

    [Fact]
    public void Plant_OnUntilledTile_IsRefused()
        => FarmRules.Plant(null, CropCatalog.Parsnip, Noon(1), s_calendar, out _).Should().Be(FarmVerdict.NotTilled);

    [Fact]
    public void Plant_OnTilledTile_StartsGrowthAtStageZero()
    {
        FarmVerdict verdict = FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, Noon(1), s_calendar, out FarmTile next);

        verdict.Should().Be(FarmVerdict.Ok);
        next.CropId.Should().Be(CropCatalog.Parsnip);
        next.PlantedAtMs.Should().Be(Noon(1));
        FarmRules.StageAt(next, Noon(1), s_calendar).Should().Be(0);
    }

    [Fact]
    public void Plant_WhereACropGrows_IsRefused()
        => FarmRules.Plant(PlantedParsnip(Noon(1)), CropCatalog.Radish, Noon(1), s_calendar, out _).Should().Be(FarmVerdict.HasCrop);

    [Fact]
    public void Plant_UnknownCrop_IsRefused()
        => FarmRules.Plant(FarmTile.Bare, 999, Noon(1), s_calendar, out _).Should().Be(FarmVerdict.UnknownCrop);

    [Fact]
    public void Water_OnUntilledTile_IsRefused()
        => FarmRules.Water(null, Noon(1), s_calendar, out _).Should().Be(FarmVerdict.NotTilled);

    [Fact]
    public void Water_OnBareSoil_WetsItUntilTheDayEnds()
    {
        FarmTile watered = WateredOn(FarmTile.Bare, 2);

        FarmRules.IsWet(watered, Midnight(2), s_calendar).Should().BeTrue();
        FarmRules.IsWet(watered, Midnight(3) - 1, s_calendar).Should().BeTrue();
        FarmRules.IsWet(watered, Midnight(3), s_calendar).Should().BeFalse("watering resets at the day rollover");
        FarmRules.IsWet(FarmTile.Bare, Noon(2), s_calendar).Should().BeFalse();
    }

    [Fact]
    public void GrowthDaysAt_NeverWatered_DoesNotGrow()
    {
        FarmTile tile = Planted(CropCatalog.Parsnip, 1);

        FarmRules.GrowthDaysAt(tile, Noon(40), s_calendar).Should().Be(0);
        FarmRules.StageAt(tile, Noon(40), s_calendar).Should().Be(0);
    }

    [Fact]
    public void GrowthDaysAt_WateredToday_CountsOnlyAfterTheDayEnds()
    {
        FarmTile tile = WateredOn(Planted(CropCatalog.Parsnip, 1), 1);

        FarmRules.GrowthDaysAt(tile, Midnight(2) - 1, s_calendar).Should().Be(0);
        FarmRules.GrowthDaysAt(tile, Midnight(2), s_calendar).Should().Be(1);
        FarmRules.StageAt(tile, Midnight(2), s_calendar).Should().Be(1);
    }

    [Fact]
    public void GrowthDaysAt_WateredDayOne_SkippedDayTwo_WateredDayThree_AdvancesTwoDays()
    {
        FarmTile tile = Planted(CropCatalog.Parsnip, 1);
        tile = WateredOn(tile, 1);
        tile = WateredOn(tile, 3);

        FarmRules.GrowthDaysAt(tile, Noon(3), s_calendar).Should().Be(1, "day 1 has ended, day 3 has not");
        FarmRules.GrowthDaysAt(tile, Midnight(4), s_calendar).Should().Be(2);
        FarmRules.GrowthDaysAt(tile, Noon(30), s_calendar).Should().Be(2, "a crop that is never watered again stops growing");
        FarmRules.StageAt(tile, Midnight(4), s_calendar).Should().Be(2);
    }

    [Fact]
    public void Water_TwiceOnTheSameDay_CountsOnce()
    {
        FarmTile tile = Planted(CropCatalog.Parsnip, 1);
        tile = WateredOn(tile, 1);
        FarmRules.Water(tile, Noon(1) + 1000, s_calendar, out FarmTile again);

        again.Should().Be(tile);
        FarmRules.GrowthDaysAt(again, Midnight(2), s_calendar).Should().Be(1);
    }

    [Fact]
    public void Water_AfterAGap_SettlesTheEarlierDayIntoTheGrowth()
    {
        FarmTile tile = WateredOn(Planted(CropCatalog.Parsnip, 1), 1);

        FarmRules.Water(tile, Noon(5), s_calendar, out FarmTile next);

        next.GrowthDays.Should().Be(1);
        next.WateredDay.Should().Be(5);
    }

    [Fact]
    public void GrowthDaysAt_CapsAtWhatTheCropNeeds()
    {
        FarmTile tile = new(CropCatalog.Parsnip, Noon(1), s_parsnipDays, 9, 0);

        FarmRules.GrowthDaysAt(tile, Noon(50), s_calendar).Should().Be(s_parsnipDays);
    }

    [Fact]
    public void Plant_OnSoilWateredToday_KeepsTodaysWater()
    {
        FarmTile tile = WateredOn(FarmTile.Bare, 1);
        FarmRules.Plant(tile, CropCatalog.Parsnip, Noon(1) + 1, s_calendar, out FarmTile planted);

        FarmRules.GrowthDaysAt(planted, Midnight(2), s_calendar).Should().Be(1);
    }

    [Fact]
    public void Plant_OnSoilWateredYesterday_DoesNotInheritThatWater()
    {
        FarmTile tile = WateredOn(FarmTile.Bare, 1);
        FarmRules.Plant(tile, CropCatalog.Parsnip, Noon(2), s_calendar, out FarmTile planted);

        FarmRules.GrowthDaysAt(planted, Midnight(30), s_calendar).Should().Be(0);
    }

    [Fact]
    public void StageAt_ParsnipWateredEveryDay_AdvancesOneStageADayAndEndsMature()
    {
        CropDefinition crop = CropCatalog.All[0];
        FarmTile tile = Planted(crop.Id, 1);

        for (int day = 1; day <= crop.TotalGrowthDays; day++)
        {
            tile = WateredOn(tile, day);
            FarmRules.StageAt(tile, Noon(day), s_calendar).Should().Be(day - 1);
            FarmRules.IsMature(tile, Noon(day), s_calendar).Should().BeFalse();
        }

        FarmRules.StageAt(tile, Midnight(crop.TotalGrowthDays + 1), s_calendar).Should().Be(crop.StageCount - 1);
        FarmRules.IsMature(tile, Midnight(crop.TotalGrowthDays + 1), s_calendar).Should().BeTrue();
        FarmRules.IsMature(tile, Midnight(crop.TotalGrowthDays + 1) - 1, s_calendar).Should().BeFalse();
    }

    [Fact]
    public void CropCatalog_Crops_NeedTheDocumentedWateredDays()
    {
        CropCatalog.All.Select(c => (c.Key, c.TotalGrowthDays)).Should().Equal(("parsnip", 4), ("radish", 6), ("cauliflower", 12));
    }

    [Fact]
    public void StageAt_RadishAndCauliflower_FollowTheirStageDays()
    {
        // Radish 1/2/1/2 and cauliflower 1/2/4/4/1: the stage changes after 1, 3, 4, 6 and 1, 3, 7, 11, 12 watered days.
        CropDefinition radish = CropCatalog.All[1];
        int[] radishStage = [.. Enumerable.Range(0, 7).Select(radish.StageAt)];
        radishStage.Should().Equal(0, 1, 1, 2, 3, 3, 4);

        CropDefinition cauliflower = CropCatalog.All[2];
        int[] cauliStage = [.. Enumerable.Range(0, 13).Select(cauliflower.StageAt)];
        cauliStage.Should().Equal(0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 5);
    }

    [Fact]
    public void Harvest_BeforeMature_IsRefused()
    {
        FarmTile planted = WateredOn(Planted(CropCatalog.Parsnip, 1), 1);

        FarmRules.Harvest(planted, Midnight(2), s_calendar, out _, out _).Should().Be(FarmVerdict.NotMature);
    }

    [Fact]
    public void Harvest_WhenMature_YieldsTheCropAndLeavesSoilTendedToday()
    {
        FarmTile ready = new(CropCatalog.Parsnip, Noon(1), s_parsnipDays, 0, 0);

        FarmVerdict verdict = FarmRules.Harvest(ready, Noon(6), s_calendar, out FarmTile next, out CropDefinition? crop);

        verdict.Should().Be(FarmVerdict.Ok);
        crop!.Id.Should().Be(CropCatalog.Parsnip);
        next.HasCrop.Should().BeFalse();
        next.TendedDay.Should().Be(6);
    }

    [Fact]
    public void Harvest_BareSoilOrUntilled_IsRefused()
    {
        FarmRules.Harvest(FarmTile.Bare, Noon(1), s_calendar, out _, out _).Should().Be(FarmVerdict.NothingToDo);
        FarmRules.Harvest(null, Noon(1), s_calendar, out _, out _).Should().Be(FarmVerdict.NotTilled);
    }

    [Fact]
    public void RevertRoll_IsDeterministicAndStable()
    {
        FarmRules.RevertRoll("farm:abc", 3, 4, 5).Should().Be(FarmRules.RevertRoll("farm:abc", 3, 4, 5));
        FarmRules.RevertRoll("farm:abc", 3, 4, 5).Should().Be(62, "the hash must not change between runs or releases: it decides real players' soil");
    }

    [Fact]
    public void RevertRoll_DependsOnInstanceTileAndDay()
    {
        int[] baseline = [.. Enumerable.Range(1, 60).Select(d => FarmRules.RevertRoll("farm:abc", 3, 4, d))];

        baseline.Should().NotEqual(Enumerable.Range(1, 60).Select(d => FarmRules.RevertRoll("farm:abd", 3, 4, d)));
        baseline.Should().NotEqual(Enumerable.Range(1, 60).Select(d => FarmRules.RevertRoll("farm:abc", 4, 3, d)));
        baseline.Distinct().Count().Should().BeGreaterThan(10);
    }

    [Fact]
    public void RevertRoll_FallsBelowTheChanceAboutThirtyPercentOfTheTime()
    {
        const int Days = 20_000;
        int hits = Enumerable.Range(1, Days).Count(d => FarmRules.RevertRoll("farm:abc", 7, 9, d) < FarmRules.RevertChancePercent);

        hits.Should().BeInRange((int)(Days * 0.28), (int)(Days * 0.32));
    }

    private static int FirstRevertingDay(string key, int x, int y, int after)
    {
        for (int day = after + 1; ; day++)
        {
            if (FarmRules.RevertRoll(key, x, y, day) < FarmRules.RevertChancePercent)
            {
                return day;
            }
        }
    }

    [Fact]
    public void IsReverted_BareDrySoil_RevertsWhenARollingDayEnds()
    {
        FarmTile tilled = FarmTile.Bare with { TendedDay = 1 };
        int hit = FirstRevertingDay("farm:abc", 3, 4, 1);

        FarmRules.IsReverted(tilled, "farm:abc", 3, 4, Midnight(1), s_calendar).Should().BeFalse("the tilling day is a day of grace");
        FarmRules.IsReverted(tilled, "farm:abc", 3, 4, Noon(hit), s_calendar).Should().BeFalse("the rolling day has not ended yet");
        FarmRules.IsReverted(tilled, "farm:abc", 3, 4, Midnight(hit + 1), s_calendar).Should().BeTrue();
        FarmRules.IsReverted(tilled, "farm:abc", 3, 4, Noon(hit + 100), s_calendar).Should().BeTrue("a reverted tile stays reverted");
    }

    [Fact]
    public void IsReverted_SameQuestionAskedTwice_GivesTheSameAnswer()
    {
        FarmTile tilled = FarmTile.Bare with { TendedDay = 2 };

        bool first = FarmRules.IsReverted(tilled, "farm:xyz", 10, 12, Noon(40), s_calendar);

        FarmRules.IsReverted(tilled, "farm:xyz", 10, 12, Noon(40), s_calendar).Should().Be(first);
    }

    [Fact]
    public void IsReverted_SoilWateredOnTheRollingDay_SurvivesThatDay()
    {
        int hit = FirstRevertingDay("farm:abc", 3, 4, 1);
        FarmTile watered = WateredOn(FarmTile.Bare with { TendedDay = 1 }, hit);

        FarmRules.IsReverted(watered, "farm:abc", 3, 4, Midnight(hit + 1), s_calendar).Should().BeFalse("a watered day is not a dry day");
    }

    [Fact]
    public void IsReverted_SoilWithACrop_NeverReverts()
    {
        FarmTile planted = Planted(CropCatalog.Parsnip, 1);

        FarmRules.IsReverted(planted, "farm:abc", 3, 4, Noon(500), s_calendar).Should().BeFalse();
    }

    [Fact]
    public void IsReverted_AfterHarvest_StartsDryDaysFromTheHarvestDay()
    {
        FarmTile ready = new(CropCatalog.Parsnip, Noon(1), s_parsnipDays, 0, 0);
        FarmRules.Harvest(ready, Noon(10), s_calendar, out FarmTile bare, out _);
        int hit = FirstRevertingDay("farm:abc", 3, 4, 10);

        FarmRules.IsReverted(bare, "farm:abc", 3, 4, Midnight(hit), s_calendar).Should().BeFalse();
        FarmRules.IsReverted(bare, "farm:abc", 3, 4, Midnight(hit + 1), s_calendar).Should().BeTrue();
    }

    // Season boundaries, from the default calendar (docs/adr/ADR-018).
    private static readonly long s_summer = s_calendar.EpochMs + s_calendar.SeasonLengthMs;
    private static readonly long s_fall = s_calendar.EpochMs + (2 * s_calendar.SeasonLengthMs);

    [Fact]
    public void Plant_CropOutOfSeason_IsRefused()
        => FarmRules.Plant(FarmTile.Bare, CropCatalog.Radish, s_calendar.EpochMs, s_calendar, out _).Should().Be(FarmVerdict.OutOfSeason);

    [Fact]
    public void Plant_CropInSeason_IsAllowed()
        => FarmRules.Plant(FarmTile.Bare, CropCatalog.Radish, s_summer, s_calendar, out _).Should().Be(FarmVerdict.Ok);

    [Fact]
    public void Plant_OnTheFirstMomentOfASeason_UsesThatSeason()
    {
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Cauliflower, s_fall - 1, s_calendar, out _).Should().Be(FarmVerdict.Ok, "last moment of summer");
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Cauliflower, s_fall, s_calendar, out _).Should().Be(FarmVerdict.OutOfSeason, "first moment of fall");
    }

    [Fact]
    public void EveryCrop_GrowsInSomeSeason_AndEverySeasonHasACrop()
    {
        foreach (CropDefinition crop in CropCatalog.All)
        {
            crop.Seasons.Should().NotBe(SeasonSet.None, crop.Key);
        }

        foreach (Season season in System.Enum.GetValues<Season>())
        {
            CropCatalog.All.Should().Contain(c => c.GrowsIn(season), $"a player must be able to plant something in {season}");
        }
    }

    [Fact]
    public void IsWithered_CropAtTheSeasonChange_WithersExactlyWhenTheNextSeasonBegins()
    {
        // Radish: summer and fall. Planted in summer, it withers when winter begins.
        FarmTile radish = new(CropCatalog.Radish, s_summer + 1000, 0, 0, 0);
        long winter = s_calendar.EpochMs + (3 * s_calendar.SeasonLengthMs);

        FarmRules.IsWithered(radish, winter - 1, s_calendar).Should().BeFalse();
        FarmRules.IsWithered(radish, winter, s_calendar).Should().BeTrue();
        FarmRules.IsWithered(radish, winter + (5 * s_calendar.SeasonLengthMs), s_calendar).Should().BeTrue("a withered crop never revives when its season comes round again");
    }

    [Fact]
    public void Harvest_MatureCropAfterItsSeasonEnded_IsRefusedAsWithered()
    {
        FarmTile ready = new(CropCatalog.Cauliflower, s_calendar.EpochMs + 1000, CropCatalog.All[2].TotalGrowthDays, 0, 0);

        FarmRules.Harvest(ready, s_fall - 1, s_calendar, out _, out _).Should().Be(FarmVerdict.Ok, "still summer");
        FarmRules.Harvest(ready, s_fall, s_calendar, out _, out _).Should().Be(FarmVerdict.Withered);
    }

    [Fact]
    public void Plant_OverAWitheredCrop_ReplacesIt()
    {
        FarmTile withered = new(CropCatalog.Cauliflower, s_calendar.EpochMs + 1000, 0, 0, 0);

        FarmRules.Plant(withered, CropCatalog.Parsnip, s_fall, s_calendar, out FarmTile next).Should().Be(FarmVerdict.Ok);
        next.CropId.Should().Be(CropCatalog.Parsnip);
        next.PlantedAtMs.Should().Be(s_fall);
    }

    [Fact]
    public void Plant_OverAHealthyCrop_StillHasCrop()
    {
        FarmTile growing = new(CropCatalog.Cauliflower, s_calendar.EpochMs + 1000, 0, 0, 0);

        FarmRules.Plant(growing, CropCatalog.Parsnip, s_summer - 1, s_calendar, out _).Should().Be(FarmVerdict.HasCrop);
    }

    [Theory]
    [InlineData(40.0, 40.0, 2, 2, 16, true)]
    [InlineData(40.0, 40.0, 2, 2, 8, true)]
    [InlineData(0.0, 0.0, 10, 10, 16, false)]
    [InlineData(40.0, 40.0, 5, 2, 16, false)]
    public void InReach_ComparesTheDistanceToTheTileCentre(double feetX, double feetY, int tileX, int tileY, int tileSize, bool expected)
        => FarmRules.InReach(feetX, feetY, tileX, tileY, tileSize).Should().Be(expected);
}
