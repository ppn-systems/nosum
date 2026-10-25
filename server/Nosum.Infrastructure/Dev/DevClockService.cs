// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Calendar;
using Nosum.Gameplay.Calendar;
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
}
