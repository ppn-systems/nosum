// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Calendar;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Weather;
using Nosum.Infrastructure.Calendar;
using Nosum.Infrastructure.Dev;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class DayClockServiceTests
{
    private static readonly GameCalendar s_calendar = GameCalendar.Default;

    // Noon of game day 3.
    private static readonly DateTimeOffset s_start = DateTimeOffset.FromUnixTimeMilliseconds(s_calendar.DayStartMs(3) + (s_calendar.DayLengthMs / 2));

    [Fact]
    public void Timer_AtTheDayBoundary_RaisesDayEndedOnce()
    {
        ManualClock clock = new(s_start);
        using DayClockService dayClock = new(s_calendar, clock);
        List<GameDayEnded> ended = [];
        dayClock.DayEnded += ended.Add;
        dayClock.Start();

        clock.Advance(TimeSpan.FromMilliseconds(s_calendar.DayLengthMs / 2 - 1));
        ended.Should().BeEmpty("the day has not ended yet");

        clock.Advance(TimeSpan.FromMilliseconds(100));

        ended.Should().Equal(new GameDayEnded(3, s_calendar.DayStartMs(4)));
        dayClock.CurrentDay.Should().Be(4);
    }

    [Fact]
    public void Timer_KeepsFiringOncePerDay()
    {
        ManualClock clock = new(s_start);
        using DayClockService dayClock = new(s_calendar, clock);
        List<int> days = [];
        dayClock.DayEnded += e => days.Add(e.DayNumber);
        dayClock.Start();

        for (int i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(s_calendar.DayLengthMs));
        }

        days.Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void Reconcile_AfterTheClockJumped_RaisesEveryDayItCrossedInOrder()
    {
        ManualClock clock = new(s_start);
        using DayClockService dayClock = new(s_calendar, clock);
        List<int> days = [];
        dayClock.DayEnded += e => days.Add(e.DayNumber);
        dayClock.Start();

        clock.Jump(TimeSpan.FromMilliseconds(s_calendar.DayLengthMs * 3));
        dayClock.Reconcile();
        dayClock.Reconcile();

        days.Should().Equal(3, 4, 5);
    }

    [Fact]
    public void DayEnded_ASubscriberThatThrows_DoesNotStopTheOthersOrTheClock()
    {
        ManualClock clock = new(s_start);
        List<Exception> errors = [];
        using DayClockService dayClock = new(s_calendar, clock, errors.Add);
        int seen = 0;
        dayClock.DayEnded += _ => throw new InvalidOperationException("boom");
        dayClock.DayEnded += _ => seen++;
        dayClock.Start();

        clock.Advance(TimeSpan.FromMilliseconds(s_calendar.DayLengthMs));
        clock.Advance(TimeSpan.FromMilliseconds(s_calendar.DayLengthMs));

        seen.Should().Be(2);
        errors.Should().HaveCount(2);
    }

    [Fact]
    public void Dispose_StopsTheEvents()
    {
        ManualClock clock = new(s_start);
        DayClockService dayClock = new(s_calendar, clock);
        int seen = 0;
        dayClock.DayEnded += _ => seen++;
        dayClock.Start();

        dayClock.Dispose();
        clock.Advance(TimeSpan.FromMilliseconds(s_calendar.DayLengthMs * 2));
        dayClock.Reconcile();

        seen.Should().Be(0);
    }

    [Fact]
    public void DevClock_AdvanceDays_MovesTheTimeAndDeliversTheDays()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        List<int> days = [];
        dayClock.DayEnded += e => days.Add(e.DayNumber);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);

        long? now = service.AdvanceDays(2);

        now.Should().Be(s_start.ToUnixTimeMilliseconds() + (2 * s_calendar.DayLengthMs));
        days.Should().Equal(3, 4);
        dayClock.CurrentDay.Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(IDevClockService.MaxDays + 1)]
    public void DevClock_AdvanceDays_OutOfRange_ChangesNothing(int days)
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        DevClockService service = new(dev, s_calendar, dayClock);

        service.AdvanceDays(days).Should().BeNull();

        dev.GetUtcNow().Should().Be(s_start);
    }

    [Fact]
    public void DevClock_AdvanceSeasons_MovesToTheStartOfEachLaterSeason()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);
        long startMs = s_start.ToUnixTimeMilliseconds();
        int startSeason = s_calendar.SeasonNumberAt(startMs);

        long? now = service.AdvanceSeasons(2);

        now.Should().Be(s_calendar.NextSeasonStartMs(s_calendar.NextSeasonStartMs(startMs)));
        s_calendar.SeasonNumberAt(now!.Value).Should().Be(startSeason + 2);
        dayClock.CurrentDay.Should().Be(s_calendar.DayNumberAt(now.Value));
    }

    [Fact]
    public void DevClock_AdvanceSeasons_Zero_MovesToTheNextSpring()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);

        long? now = service.AdvanceSeasons(0);

        // Whatever season the run starts in, zero lands on Spring and never passes a season it is already in.
        now.Should().NotBeNull();
        s_calendar.SeasonAt(now!.Value).Should().Be(Season.Spring);
        dev.GetUtcNow().ToUnixTimeMilliseconds().Should().Be(now.Value);
        if (s_calendar.SeasonAt(s_start.ToUnixTimeMilliseconds()) == Season.Spring)
        {
            now.Value.Should().Be(s_start.ToUnixTimeMilliseconds());
        }
    }

    [Fact]
    public void DevClock_AdvanceSeasons_ZeroInSpring_ChangesNothing()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);
        long spring = service.AdvanceSeasons(0)!.Value;

        long? again = service.AdvanceSeasons(0);

        again.Should().Be(spring);
        s_calendar.SeasonAt(spring).Should().Be(Season.Spring);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(IDevClockService.MaxSeasons + 1)]
    public void DevClock_AdvanceSeasons_OutOfRange_ChangesNothing(int seasons)
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);

        service.AdvanceSeasons(seasons).Should().BeNull();
        dev.GetUtcNow().Should().Be(s_start);
    }

    [Fact]
    public void DevClock_FreezeClock_HoldsTheClockUntilItIsResumed()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);

        service.FreezeClock(true).Should().Be(s_start.ToUnixTimeMilliseconds());
        inner.Advance(TimeSpan.FromHours(1));

        dev.GetUtcNow().Should().Be(s_start, "a pinned clock does not move while the server runs");

        // Resuming continues from the pinned instant: the held hour does not count as elapsed.
        service.FreezeClock(false);
        dev.GetUtcNow().Should().Be(s_start);
        inner.Advance(TimeSpan.FromMinutes(1));
        dev.GetUtcNow().Should().Be(s_start + TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void DevClock_FreezeClock_StillMovesWhenTheTesterAdvancesIt()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);
        service.FreezeClock(true);

        long? now = service.AdvanceDays(1);

        now.Should().Be(s_start.ToUnixTimeMilliseconds() + s_calendar.DayLengthMs);
        dayClock.CurrentDay.Should().Be(s_calendar.DayNumberAt(now!.Value));
    }

    [Fact]
    public void DevClock_SkipToSunnyDay_LandsAtNoonOfASunnyDay()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);
        long startMs = s_start.ToUnixTimeMilliseconds();

        long now = service.SkipToSunnyDay();

        WeatherRules.At(s_calendar, now).Should().Be(WeatherKind.Sun, "the tester asked for a day without rain");
        now.Should().BeGreaterThanOrEqualTo(s_calendar.DayStartMs(s_calendar.DayNumberAt(now)) + (s_calendar.DayLengthMs / 2), "the run is in daylight");
        if (WeatherRules.At(s_calendar, startMs) == WeatherKind.Sun)
        {
            s_calendar.DayNumberAt(now).Should().Be(s_calendar.DayNumberAt(startMs), "a sunny day is kept as it is");
        }
        else
        {
            s_calendar.DayNumberAt(now).Should().BeGreaterThan(s_calendar.DayNumberAt(startMs));
        }
    }

    [Fact]
    public void DevClock_SkipToSunnyDay_AfterNoon_KeepsItsTime()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);
        long startMs = s_start.ToUnixTimeMilliseconds();
        inner.Advance(TimeSpan.FromHours(3));
        if (WeatherRules.At(s_calendar, startMs) != WeatherKind.Sun)
        {
            return;
        }

        long now = service.SkipToSunnyDay();

        now.Should().Be(startMs + (long)TimeSpan.FromHours(3).TotalMilliseconds);
    }

    [Fact]
    public void DevClock_SkipToSunnyDay_WhileFrozen_KeepsTheDayItLandedOn()
    {
        ManualClock inner = new(s_start);
        DevTimeProvider dev = new(inner);
        using DayClockService dayClock = new(s_calendar, dev);
        dayClock.Start();
        DevClockService service = new(dev, s_calendar, dayClock);
        long now = service.SkipToSunnyDay();
        service.FreezeClock(true);

        inner.Advance(TimeSpan.FromDays(3));

        dev.GetUtcNow().ToUnixTimeMilliseconds().Should().Be(now, "the run stays on the sunny day while the clock is pinned");
        WeatherRules.At(s_calendar, dev.GetUtcNow().ToUnixTimeMilliseconds()).Should().Be(WeatherKind.Sun);
    }

    // A clock whose timers fire when the test advances it, so the day boundary can be crossed without waiting.
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        // Moves time and fires the timers that fell due, as a real timer thread would.
        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (ManualTimer timer in _timers.ToArray())
            {
                timer.FireIfDue(_now);
            }
        }

        // Moves time without firing anything: the clock was set, not waited out.
        public void Jump(TimeSpan by) => _now += by;

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_due is { } due && now >= due)
                {
                    _due = null;
                    callback(state);
                }
            }

            public void Dispose() => _due = null;

            public ValueTask DisposeAsync()
            {
                this.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
