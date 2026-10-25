// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class FarmRulesTests
{
    private const long T0 = 1_000_000;
    private static readonly GameCalendar s_calendar = GameCalendar.Default;
    private static readonly long s_parsnipMs = CropCatalog.All[0].TotalGrowthMs;

    private static FarmTile PlantedParsnip(long plantedAt, long wetUntil = 0)
        => new(CropCatalog.Parsnip, plantedAt, 0, plantedAt, wetUntil);

    [Fact]
    public void Till_OnTillableUntilledTile_GivesBareSoil()
    {
        FarmVerdict verdict = FarmRules.Till(true, null, out FarmTile next);

        verdict.Should().Be(FarmVerdict.Ok);
        next.Should().Be(FarmTile.Bare);
    }

    [Fact]
    public void Till_OnGroundThatIsNotTillable_IsRefused()
        => FarmRules.Till(false, null, out _).Should().Be(FarmVerdict.NotTillable);

    [Fact]
    public void Till_OnTilledTile_IsRefused()
        => FarmRules.Till(true, FarmTile.Bare, out _).Should().Be(FarmVerdict.AlreadyTilled);

    [Fact]
    public void Plant_OnUntilledTile_IsRefused()
        => FarmRules.Plant(null, CropCatalog.Parsnip, T0, s_calendar, out _).Should().Be(FarmVerdict.NotTilled);

    [Fact]
    public void Plant_OnTilledTile_StartsGrowthAtStageZero()
    {
        FarmVerdict verdict = FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, T0, s_calendar, out FarmTile next);

        verdict.Should().Be(FarmVerdict.Ok);
        next.CropId.Should().Be(CropCatalog.Parsnip);
        next.PlantedAtMs.Should().Be(T0);
        FarmRules.StageAt(next, T0).Should().Be(0);
    }

    [Fact]
    public void Plant_WhereACropGrows_IsRefused()
        => FarmRules.Plant(PlantedParsnip(T0), CropCatalog.Radish, T0, s_calendar, out _).Should().Be(FarmVerdict.HasCrop);

    [Fact]
    public void Plant_UnknownCrop_IsRefused()
        => FarmRules.Plant(FarmTile.Bare, 999, T0, s_calendar, out _).Should().Be(FarmVerdict.UnknownCrop);

    [Fact]
    public void Water_OnUntilledTile_IsRefused()
        => FarmRules.Water(null, T0, out _).Should().Be(FarmVerdict.NotTilled);

    [Fact]
    public void Water_OnBareSoil_WetsItForTheWateringDuration()
    {
        FarmVerdict verdict = FarmRules.Water(FarmTile.Bare, T0, out FarmTile next);

        verdict.Should().Be(FarmVerdict.Ok);
        FarmRules.IsWet(next, T0 + FarmRules.WetDurationMs - 1).Should().BeTrue();
        FarmRules.IsWet(next, T0 + FarmRules.WetDurationMs).Should().BeFalse();
    }

    [Fact]
    public void GrowthAt_DrySoil_DoesNotGrow()
    {
        FarmTile tile = PlantedParsnip(T0);

        FarmRules.GrowthAt(tile, T0 + 10 * s_parsnipMs).Should().Be(0);
        FarmRules.StageAt(tile, T0 + 10 * s_parsnipMs).Should().Be(0);
    }

    [Fact]
    public void GrowthAt_WateredSoil_GrowsWhileWetAndStopsWhenItDries()
    {
        FarmRules.Water(FarmTile.Bare, T0, out FarmTile wet);
        FarmRules.Plant(wet, CropCatalog.Parsnip, T0, s_calendar, out FarmTile planted);

        FarmRules.GrowthAt(planted, T0 + 20_000).Should().Be(20_000);
        FarmRules.GrowthAt(planted, T0 + FarmRules.WetDurationMs).Should().Be(System.Math.Min(FarmRules.WetDurationMs, s_parsnipMs));
        FarmRules.GrowthAt(planted, T0 + FarmRules.WetDurationMs + 500_000).Should().Be(System.Math.Min(FarmRules.WetDurationMs, s_parsnipMs), "a dry crop stops growing");
    }

    [Fact]
    public void GrowthAt_WateredLaterThanPlanting_CountsOnlyTheWetTime()
    {
        FarmRules.Plant(FarmTile.Bare, CropCatalog.Parsnip, T0, s_calendar, out FarmTile planted);
        FarmRules.Water(planted, T0 + 30_000, out FarmTile watered);

        FarmRules.GrowthAt(watered, T0 + 30_000).Should().Be(0);
        FarmRules.GrowthAt(watered, T0 + 40_000).Should().Be(10_000);
    }

    [Fact]
    public void GrowthAt_WateredTwice_AddsBothWetPeriods()
    {
        // Cauliflower needs more growth than one watering lasts.
        CropDefinition slow = CropCatalog.All[2];
        slow.TotalGrowthMs.Should().BeGreaterThan(FarmRules.WetDurationMs);
        FarmRules.Water(FarmTile.Bare, T0, out FarmTile wet);
        FarmRules.Plant(wet, slow.Id, T0, s_calendar, out FarmTile planted);
        long dry = T0 + FarmRules.WetDurationMs + 50_000;
        FarmRules.GrowthAt(planted, dry).Should().Be(FarmRules.WetDurationMs);

        FarmRules.Water(planted, dry, out FarmTile again);

        FarmRules.GrowthAt(again, dry).Should().Be(FarmRules.WetDurationMs);
        FarmRules.GrowthAt(again, dry + 10_000).Should().Be(FarmRules.WetDurationMs + 10_000);
        FarmRules.IsMature(again, dry + FarmRules.WetDurationMs).Should().BeTrue();
    }

    [Fact]
    public void StageAt_AdvancesThroughEveryStageAndEndsMature()
    {
        CropDefinition crop = CropCatalog.All[0];
        FarmTile tile = new(crop.Id, T0, 0, T0, long.MaxValue / 2);
        int last = -1;

        for (long t = 0; t <= crop.TotalGrowthMs; t += 1000)
        {
            int stage = FarmRules.StageAt(tile, T0 + t);
            stage.Should().BeGreaterThanOrEqualTo(last);
            last = stage;
        }

        last.Should().Be(crop.StageCount - 1);
        FarmRules.IsMature(tile, T0 + crop.TotalGrowthMs).Should().BeTrue();
        FarmRules.IsMature(tile, T0 + crop.TotalGrowthMs - 1).Should().BeFalse();
    }

    [Fact]
    public void Harvest_BeforeMature_IsRefused()
    {
        FarmRules.Water(FarmTile.Bare, T0, out FarmTile wet);
        FarmRules.Plant(wet, CropCatalog.Parsnip, T0, s_calendar, out FarmTile planted);

        FarmRules.Harvest(planted, T0 + 1000, s_calendar, out _, out _).Should().Be(FarmVerdict.NotMature);
    }

    [Fact]
    public void Harvest_WhenMature_YieldsTheCropAndLeavesTilledSoil()
    {
        FarmTile ready = new(CropCatalog.Parsnip, T0, s_parsnipMs, T0, 0);

        FarmVerdict verdict = FarmRules.Harvest(ready, T0 + 1, s_calendar, out FarmTile next, out CropDefinition? crop);

        verdict.Should().Be(FarmVerdict.Ok);
        crop!.Id.Should().Be(CropCatalog.Parsnip);
        next.HasCrop.Should().BeFalse();
    }

    [Fact]
    public void Harvest_BareSoilOrUntilled_IsRefused()
    {
        FarmRules.Harvest(FarmTile.Bare, T0, s_calendar, out _, out _).Should().Be(FarmVerdict.NothingToDo);
        FarmRules.Harvest(null, T0, s_calendar, out _, out _).Should().Be(FarmVerdict.NotTilled);
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
        FarmTile radish = new(CropCatalog.Radish, s_summer + 1000, 0, s_summer + 1000, 0);
        long winter = s_calendar.EpochMs + (3 * s_calendar.SeasonLengthMs);

        FarmRules.IsWithered(radish, winter - 1, s_calendar).Should().BeFalse();
        FarmRules.IsWithered(radish, winter, s_calendar).Should().BeTrue();
        FarmRules.IsWithered(radish, winter + (5 * s_calendar.SeasonLengthMs), s_calendar).Should().BeTrue("a withered crop never revives when its season comes round again");
    }

    [Fact]
    public void Harvest_MatureCropAfterItsSeasonEnded_IsRefusedAsWithered()
    {
        FarmTile ready = new(CropCatalog.Cauliflower, s_calendar.EpochMs + 1000, CropCatalog.All[2].TotalGrowthMs, s_calendar.EpochMs + 1000, 0);

        FarmRules.Harvest(ready, s_fall - 1, s_calendar, out _, out _).Should().Be(FarmVerdict.Ok, "still summer");
        FarmRules.Harvest(ready, s_fall, s_calendar, out _, out _).Should().Be(FarmVerdict.Withered);
    }

    [Fact]
    public void Plant_OverAWitheredCrop_ReplacesIt()
    {
        FarmTile withered = new(CropCatalog.Cauliflower, s_calendar.EpochMs + 1000, 0, s_calendar.EpochMs + 1000, 0);

        FarmRules.Plant(withered, CropCatalog.Parsnip, s_fall, s_calendar, out FarmTile next).Should().Be(FarmVerdict.Ok);
        next.CropId.Should().Be(CropCatalog.Parsnip);
        next.PlantedAtMs.Should().Be(s_fall);
    }

    [Fact]
    public void Plant_OverAHealthyCrop_StillHasCrop()
    {
        FarmTile growing = new(CropCatalog.Cauliflower, s_calendar.EpochMs + 1000, 0, s_calendar.EpochMs + 1000, 0);

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
