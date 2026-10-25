// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The server calendar: pure functions from an instant and an epoch, checked at every boundary (docs/adr/ADR-018).</summary>
public sealed class GameCalendarTests
{
    private const long Hour = 3_600_000;
    private const long Day = 12 * Hour;
    private const long SeasonMs = 28 * Day;
    private const long Epoch = 1_800_000_000_000;

    private static readonly GameCalendar s_calendar = new(Epoch, Day);

    [Fact]
    public void At_Epoch_IsYearOneSpringDayOneMidnight()
        => s_calendar.At(Epoch).Should().Be(new GameDate(1, Season.Spring, 1, 0));

    [Fact]
    public void At_BeforeTheEpoch_CountsAsTheEpoch()
        => s_calendar.At(Epoch - SeasonMs).Should().Be(new GameDate(1, Season.Spring, 1, 0));

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(Day / 24, 60)]
    [InlineData(Day / 2, 12 * 60)]
    [InlineData(Day - 1, (24 * 60) - 1)]
    public void At_WithinTheFirstDay_GivesTheGameTimeOfDay(long offset, int minuteOfDay)
    {
        GameDate date = s_calendar.At(Epoch + offset);

        date.DayOfSeason.Should().Be(1);
        date.MinuteOfDay.Should().Be(minuteOfDay);
    }

    [Fact]
    public void At_ExactlyOneDayLater_IsDayTwoMidnight()
        => s_calendar.At(Epoch + Day).Should().Be(new GameDate(1, Season.Spring, 2, 0));

    [Fact]
    public void At_LastMomentOfASeason_IsDayTwentyEightLastMinute()
        => s_calendar.At(Epoch + SeasonMs - 1).Should().Be(new GameDate(1, Season.Spring, 28, (24 * 60) - 1));

    [Fact]
    public void At_SeasonChange_StartsSummerDayOne()
        => s_calendar.At(Epoch + SeasonMs).Should().Be(new GameDate(1, Season.Summer, 1, 0));

    [Theory]
    [InlineData(0, Season.Spring)]
    [InlineData(1, Season.Summer)]
    [InlineData(2, Season.Fall)]
    [InlineData(3, Season.Winter)]
    public void SeasonAt_FollowsTheYearOrder(int seasonsAfterEpoch, Season expected)
        => s_calendar.SeasonAt(Epoch + (seasonsAfterEpoch * SeasonMs)).Should().Be(expected);

    [Fact]
    public void At_YearChange_StartsYearTwoSpring()
    {
        s_calendar.At(Epoch + (4 * SeasonMs) - 1).Should().Be(new GameDate(1, Season.Winter, 28, (24 * 60) - 1));
        s_calendar.At(Epoch + (4 * SeasonMs)).Should().Be(new GameDate(2, Season.Spring, 1, 0));
    }

    [Fact]
    public void At_ManyYearsLater_StaysConsistent()
        => s_calendar.At(Epoch + (4 * 10 * SeasonMs) + (2 * SeasonMs) + (5 * Day)).Should().Be(new GameDate(11, Season.Fall, 6, 0));

    [Fact]
    public void Default_SeasonLasts_TwoRealWeeks()
        => TimeSpan.FromMilliseconds(GameCalendar.Default.SeasonLengthMs).Should().Be(TimeSpan.FromDays(14));

    [Fact]
    public void Default_Epoch_IsAMondayMidnightUtc()
    {
        DateTimeOffset epoch = DateTimeOffset.FromUnixTimeMilliseconds(GameCalendar.DefaultEpochMs);

        epoch.DayOfWeek.Should().Be(DayOfWeek.Monday);
        epoch.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Constructor_DayShorterThanOneMillisecondPerGameMinute_IsRefused()
    {
        Action tooShort = () => _ = new GameCalendar(0, GameCalendar.MinutesPerDay - 1);

        tooShort.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SeasonBounds_AreTheStartOfThisAndTheNextSeason()
    {
        long inFall = Epoch + (2 * SeasonMs) + (9 * Day) + 123;

        s_calendar.SeasonStartMs(inFall).Should().Be(Epoch + (2 * SeasonMs));
        s_calendar.NextSeasonStartMs(inFall).Should().Be(Epoch + (3 * SeasonMs));
    }

    [Fact]
    public void WitherAt_CropPlantedInSpring_WithersWhenTheFirstSeasonItSkipsBegins()
    {
        SeasonSet springSummer = SeasonSet.Spring | SeasonSet.Summer;

        s_calendar.WitherAtMs(Epoch + Day, springSummer).Should().Be(Epoch + (2 * SeasonMs));
        s_calendar.WitherAtMs(Epoch + SeasonMs + Day, springSummer).Should().Be(Epoch + (2 * SeasonMs), "planted in summer, same fall");
    }

    [Fact]
    public void WitherAt_CropThatSkipsOnlyWinter_SurvivesTheYearWrap()
    {
        SeasonSet notWinter = SeasonSet.Spring | SeasonSet.Summer | SeasonSet.Fall;

        s_calendar.WitherAtMs(Epoch + (2 * SeasonMs), notWinter).Should().Be(Epoch + (3 * SeasonMs));
    }

    [Fact]
    public void WitherAt_CropPlantedInWinterThatGrowsInSpring_WithersAtTheNextSkippedSeasonAfterThat()
    {
        SeasonSet winterSpring = SeasonSet.Winter | SeasonSet.Spring;

        // Planted in winter of year 1: spring of year 2 is fine, summer of year 2 ends it.
        s_calendar.WitherAtMs(Epoch + (3 * SeasonMs), winterSpring).Should().Be(Epoch + (5 * SeasonMs));
    }

    [Fact]
    public void WitherAt_CropThatGrowsInEverySeason_NeverWithers()
        => s_calendar.WitherAtMs(Epoch, SeasonSet.All).Should().Be(long.MaxValue);

    [Fact]
    public void WitherAt_CropPlantedOutOfItsSeason_HasWitheredAlready()
        => s_calendar.WitherAtMs(Epoch + SeasonMs, SeasonSet.Spring).Should().Be(Epoch + SeasonMs);
}
