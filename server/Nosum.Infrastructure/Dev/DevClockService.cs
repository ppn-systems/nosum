// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Calendar;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Weather;
using Nosum.Infrastructure.Calendar;

namespace Nosum.Infrastructure.Dev;

/// <summary><see cref="IDevClockService"/> on top of <see cref="DevTimeProvider"/> and <see cref="DayClockService"/>. Registered by the host only in Development.</summary>
public sealed class DevClockService : IDevClockService
{
    private readonly DevTimeProvider _time;
    private readonly GameCalendar _calendar;
    private readonly DayClockService _dayClock;

    /// <summary>Initializes the service.</summary>
    /// <param name="time">The shifted clock every service reads.</param>
    /// <param name="calendar">The server calendar, for the length of a day.</param>
    /// <param name="dayClock">Told to deliver the days the jump crossed.</param>
    public DevClockService(DevTimeProvider time, GameCalendar calendar, DayClockService dayClock)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(dayClock);

        _time = time;
        _calendar = calendar;
        _dayClock = dayClock;
    }

    /// <inheritdoc/>
    public long? AdvanceDays(int days)
    {
        if (days < 1 || days > IDevClockService.MaxDays)
        {
            return null;
        }

        _time.Advance(TimeSpan.FromMilliseconds(_calendar.DayLengthMs * days));
        _dayClock.Reconcile();
        return _time.GetUtcNow().ToUnixTimeMilliseconds();
    }

    /// <inheritdoc/>
    public long? AdvanceSeasons(int seasons)
    {
        if (seasons is < 0 or > IDevClockService.MaxSeasons)
        {
            return null;
        }

        if (seasons == 0)
        {
            // Zero means "to the next Spring": one pass per season between the current one and the year's first season (0 to 3 passes).
            long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            seasons = (GameCalendar.SeasonsPerYear - (int)_calendar.SeasonAt(nowMs)) % GameCalendar.SeasonsPerYear;
        }

        // Season starts fall on day boundaries (the epoch is one), so each jump is a whole number of game days.
        for (int i = 0; i < seasons; i++)
        {
            long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
            _time.Advance(TimeSpan.FromMilliseconds(_calendar.NextSeasonStartMs(nowMs) - nowMs));
            _dayClock.Reconcile();
        }

        return _time.GetUtcNow().ToUnixTimeMilliseconds();
    }

    /// <inheritdoc/>
    public long FreezeClock(bool frozen)
    {
        if (frozen)
        {
            _time.Freeze();
        }
        else
        {
            _time.Resume();
        }

        return _time.GetUtcNow().ToUnixTimeMilliseconds();
    }

    /// <inheritdoc/>
    public long SkipToSunnyDay()
    {
        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        int today = _calendar.DayNumberAt(nowMs);
        int day = today;
        if (WeatherRules.ForDay(_calendar, today) != WeatherKind.Sun)
        {
            // The first days of every season are sunny (WeatherRules.SunnyDaysAtSeasonStart), so a sunny day is always within a season's length.
            day = today + 1;
            while (day - today <= IDevClockService.MaxDays && WeatherRules.ForDay(_calendar, day) != WeatherKind.Sun)
            {
                day++;
            }
        }

        // The middle of the sunny day, so a QA run is in daylight (a frozen midnight reads as night in every frame). A run already past noon on
        // a sunny day keeps its time.
        long noonMs = _calendar.DayStartMs(day) + (_calendar.DayLengthMs / 2);
        long targetMs = day == today ? Math.Max(nowMs, noonMs) : noonMs;
        if (targetMs > nowMs)
        {
            _time.Advance(TimeSpan.FromMilliseconds(targetMs - nowMs));
            _dayClock.Reconcile();
        }

        return _time.GetUtcNow().ToUnixTimeMilliseconds();
    }
}
