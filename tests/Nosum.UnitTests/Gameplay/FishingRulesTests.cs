// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The catch rules: timing judged on the server clock, and the seeded roll (docs/adr/ADR-071 D4, D5).</summary>
public sealed class FishingRulesTests
{
    private const int Bite = 4000;
    private const int Window = 800;

    [Fact]
    public void Judge_WellBeforeTheBite_IsTooEarly() => FishingRules.Judge(Bite - FishingRules.EarlyToleranceMs - 1, Bite, Window).Should().Be(HookVerdict.TooEarly);

    [Fact]
    public void Judge_TheEarlyTolerance_IsStillHooked() => FishingRules.Judge(Bite - FishingRules.EarlyToleranceMs, Bite, Window).Should().Be(HookVerdict.Hooked);

    [Fact]
    public void Judge_InsideTheWindow_IsHooked() => FishingRules.Judge(Bite + Window / 2, Bite, Window).Should().Be(HookVerdict.Hooked);

    [Fact]
    public void Judge_TheLateTolerance_IsStillHooked() => FishingRules.Judge(Bite + Window + FishingRules.LateToleranceMs, Bite, Window).Should().Be(HookVerdict.Hooked);

    [Fact]
    public void Judge_PastTheWindowAndTolerance_Escapes() => FishingRules.Judge(Bite + Window + FishingRules.LateToleranceMs + 1, Bite, Window).Should().Be(HookVerdict.Escaped);

    [Fact]
    public void BiteDelayMs_ForAnySeed_StaysInItsRange()
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            int delay = FishingRules.BiteDelayMs(seed);

            delay.Should().BeInRange(FishingRules.BiteMinMs, FishingRules.BiteMinMs + FishingRules.BiteSpreadMs - 1);
        }
    }

    [Fact]
    public void BiteDelayMs_SameSeed_IsTheSameDelay() => FishingRules.BiteDelayMs(42).Should().Be(FishingRules.BiteDelayMs(42));

    [Fact]
    public void WindowMs_AtLevelZero_IsTheLongestWindow() => FishingRules.WindowMs(0).Should().Be(FishingRules.WindowMaxMs);

    [Fact]
    public void WindowMs_AtTopLevel_IsTheShortestWindow() => FishingRules.WindowMs(SkillRules.MaxLevel).Should().Be(FishingRules.WindowMinMs);

    [Fact]
    public void WindowMs_HigherLevel_NeverLengthensTheWindow()
    {
        for (int level = 1; level <= SkillRules.MaxLevel; level++)
        {
            FishingRules.WindowMs(level).Should().BeLessOrEqualTo(FishingRules.WindowMs(level - 1));
        }
    }

    [Fact]
    public void SeedOf_ChangedTile_GivesAnotherSeed()
    {
        ulong first = FishingRules.SeedOf(7, 1, 1_000, 3, 4);
        ulong second = FishingRules.SeedOf(7, 1, 1_000, 3, 5);

        second.Should().NotBe(first);
    }

    [Fact]
    public void SeedOf_SameInputs_GivesTheSameSeed() => FishingRules.SeedOf(7, 1, 1_000, 3, 4).Should().Be(FishingRules.SeedOf(7, 1, 1_000, 3, 4));

    [Fact]
    public void Roll_SameSpotAndSeed_GivesTheSameCatch()
    {
        FishingSpot spot = new(FishingWater.Woods, Season.Fall, WeatherKind.Rain, 21, 5);

        for (ulong seed = 1; seed <= 200; seed++)
        {
            FishingRules.Roll(spot, seed).Should().Be(FishingRules.Roll(spot, seed));
        }
    }

    [Fact]
    public void Roll_OutOfSeason_NeverCatchesAFishOfAnotherSeason()
    {
        FishingSpot spot = new(FishingWater.Beach, Season.Summer, WeatherKind.Sun, 12, 3);

        for (ulong seed = 1; seed <= 3000; seed++)
        {
            FishSpecies? species = FishingRules.Roll(spot, seed);
            if (species is not null)
            {
                species.Seasons.Contains(Season.Summer).Should().BeTrue(species.Key);
            }
        }
    }

    [Fact]
    public void Roll_DaytimeSpot_NeverCatchesANightOnlyFish()
    {
        FishingSpot spot = new(FishingWater.Woods, Season.Winter, WeatherKind.Sun, 12, 3);

        for (ulong seed = 1; seed <= 3000; seed++)
        {
            FishingRules.Roll(spot, seed)?.Time.Should().NotBe(FishTime.Night);
        }
    }

    [Fact]
    public void Roll_SunnyDay_NeverCatchesAWatersOnlyFish()
    {
        FishingSpot spot = new(FishingWater.Woods, Season.Spring, WeatherKind.Sun, 12, 3);

        for (ulong seed = 1; seed <= 3000; seed++)
        {
            FishingRules.Roll(spot, seed)?.Weather.Should().NotBe(FishWeather.Waters);
        }
    }

    [Fact]
    public void Roll_RainyNight_CanCatchANightFishThatNeedsRain()
    {
        FishingSpot spot = new(FishingWater.Woods, Season.Fall, WeatherKind.Rain, 23, 10);

        bool caught = false;
        for (ulong seed = 1; seed <= 3000 && !caught; seed++)
        {
            caught = FishingRules.Roll(spot, seed) is { Key: "eel" };
        }

        caught.Should().BeTrue();
    }

    [Fact]
    public void Roll_WaterWithNoSpecies_GivesNothing() => FishingRules.Roll(new FishingSpot(FishingWater.None, Season.Spring, WeatherKind.Sun, 12, 3), 1).Should().BeNull();

    [Fact]
    public void Roll_HigherLevel_CatchesMoreRareFish()
    {
        static int RareCatches(int level)
        {
            FishingSpot spot = new(FishingWater.Farm | FishingWater.Woods | FishingWater.Beach, Season.Spring, WeatherKind.Rain, 21, level);
            int count = 0;
            for (ulong seed = 1; seed <= 20_000; seed++)
            {
                if (FishingRules.Roll(spot, seed) is { Rarity: FishRarity.Rare or FishRarity.Legendary })
                {
                    count++;
                }
            }

            return count;
        }

        RareCatches(10).Should().BeGreaterThan(RareCatches(0));
    }

    [Fact]
    public void Roll_HigherLevel_CatchesNothingLessOften()
    {
        static int Nothing(int level)
        {
            FishingSpot spot = new(FishingWater.Beach, Season.Spring, WeatherKind.Sun, 12, level);
            int count = 0;
            for (ulong seed = 1; seed <= 20_000; seed++)
            {
                if (FishingRules.Roll(spot, seed) is null)
                {
                    count++;
                }
            }

            return count;
        }

        Nothing(10).Should().BeLessThan(Nothing(0));
    }

    [Theory]
    [InlineData(FishRarity.Common, 5)]
    [InlineData(FishRarity.Uncommon, 10)]
    [InlineData(FishRarity.Rare, 20)]
    [InlineData(FishRarity.Legendary, 40)]
    public void CatchXp_ByRarity_GivesTheTableValue(FishRarity rarity, int expected)
    {
        FishSpecies species = new(ItemCatalog.Carp, "test", rarity, 1, SeasonSet.Spring, FishingWater.Farm);

        FishingRules.CatchXp(species).Should().Be(expected);
    }

    [Fact]
    public void CatchXp_Nothing_GivesAFewPointsForTrying() => FishingRules.CatchXp(null).Should().Be(1);

    [Theory]
    [InlineData(5, FishTime.Night)]
    [InlineData(6, FishTime.Day)]
    [InlineData(18, FishTime.Day)]
    [InlineData(19, FishTime.Night)]
    public void TimeOf_Hour_SplitsDayFromNight(int hour, FishTime expected) => FishingRules.TimeOf(hour).Should().Be(expected);
}
