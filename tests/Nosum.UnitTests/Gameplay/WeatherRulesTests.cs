// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The weather is a pure seeded function of the calendar epoch and the day (docs/adr/ADR-027 D1).</summary>
public sealed class WeatherRulesTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    // Counts the weather over many years of days, so the rates can be compared with the data.
    private static Dictionary<WeatherKind, int> Count(GameCalendar calendar, Season season, int years)
    {
        Dictionary<WeatherKind, int> counts = [];
        for (int year = 0; year < years; year++)
        {
            int first = (((year * GameCalendar.SeasonsPerYear) + (int)season) * GameCalendar.DaysPerSeason) + 1;
            for (int day = first + WeatherRules.SunnyDaysAtSeasonStart; day < first + GameCalendar.DaysPerSeason; day++)
            {
                WeatherKind kind = WeatherRules.ForDay(calendar, day);
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
        }

        return counts;
    }

    [Fact]
    public void ForDay_SameCalendarAndDay_IsAlwaysTheSame()
    {
        for (int day = 1; day <= 400; day++)
        {
            WeatherRules.ForDay(s_calendar, day).Should().Be(WeatherRules.ForDay(new GameCalendar(s_calendar.EpochMs, s_calendar.DayLengthMs), day));
        }
    }

    [Fact]
    public void ForDay_DifferentEpochs_GiveDifferentWeather()
    {
        GameCalendar other = new(s_calendar.EpochMs + 86_400_000, s_calendar.DayLengthMs);
        int differing = 0;
        for (int day = 1; day <= 400; day++)
        {
            differing += WeatherRules.ForDay(s_calendar, day) != WeatherRules.ForDay(other, day) ? 1 : 0;
        }

        differing.Should().BeGreaterThan(20);
    }

    [Fact]
    public void ForDay_FirstTwoDaysOfEverySeason_AreSunny()
    {
        for (int season = 0; season < 40; season++)
        {
            int first = (season * GameCalendar.DaysPerSeason) + 1;
            WeatherRules.ForDay(s_calendar, first).Should().Be(WeatherKind.Sun);
            WeatherRules.ForDay(s_calendar, first + 1).Should().Be(WeatherKind.Sun);
        }
    }

    [Fact]
    public void ForDay_OnlyStormsInSummerAndOnlySnowInWinter()
    {
        for (int day = 1; day <= 28 * 4 * 30; day++)
        {
            WeatherKind kind = WeatherRules.ForDay(s_calendar, day);
            Season season = (Season)(((day - 1) / GameCalendar.DaysPerSeason) % GameCalendar.SeasonsPerYear);
            if (kind == WeatherKind.Storm)
            {
                season.Should().Be(Season.Summer);
            }

            if (kind == WeatherKind.Snow)
            {
                season.Should().Be(Season.Winter);
            }

            if (season == Season.Winter)
            {
                kind.Should().NotBe(WeatherKind.Rain);
            }
        }
    }

    [Theory]
    [InlineData(Season.Spring, 20, 0, 0)]
    [InlineData(Season.Summer, 12, 5, 0)]
    [InlineData(Season.Fall, 20, 0, 0)]
    [InlineData(Season.Winter, 0, 0, 25)]
    public void ChancesOf_AreTheDocumentedRates(Season season, int rain, int storm, int snow)
        => WeatherRules.ChancesOf(season).Should().Be((rain, storm, snow));

    [Theory]
    [InlineData(Season.Spring, WeatherKind.Rain, 20)]
    [InlineData(Season.Summer, WeatherKind.Rain, 12)]
    [InlineData(Season.Summer, WeatherKind.Storm, 5)]
    [InlineData(Season.Fall, WeatherKind.Rain, 20)]
    [InlineData(Season.Winter, WeatherKind.Snow, 25)]
    public void ForDay_OverManyYears_MatchesTheSeasonRate(Season season, WeatherKind kind, int percent)
    {
        const int years = 400;
        Dictionary<WeatherKind, int> counts = Count(s_calendar, season, years);

        // Days that can roll: all but the always-sunny first two of a season.
        double rolled = years * (GameCalendar.DaysPerSeason - WeatherRules.SunnyDaysAtSeasonStart);
        (counts.GetValueOrDefault(kind) / rolled * 100).Should().BeApproximately(percent, 1.5);
    }

    [Fact]
    public void Waters_IsTrueForRainAndStormOnly()
    {
        WeatherRules.Waters(WeatherKind.Rain).Should().BeTrue();
        WeatherRules.Waters(WeatherKind.Storm).Should().BeTrue();
        WeatherRules.Waters(WeatherKind.Sun).Should().BeFalse();
        WeatherRules.Waters(WeatherKind.Snow).Should().BeFalse();
    }

    [Fact]
    public void At_AnInstant_IsTheWeatherOfItsGameDay()
    {
        for (int day = 1; day <= 200; day++)
        {
            WeatherRules.At(s_calendar, s_calendar.DayStartMs(day) + (s_calendar.DayLengthMs / 3)).Should().Be(WeatherRules.ForDay(s_calendar, day));
        }
    }

    [Fact]
    public void ForDay_BeforeTheFirstDay_CountsAsDayOne()
        => WeatherRules.ForDay(s_calendar, -5).Should().Be(WeatherKind.Sun);
}
