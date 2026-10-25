// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Calendar;
using Nosum.Gameplay.Calendar;

namespace Nosum.Infrastructure.Calendar;

/// <summary><see cref="ICalendarService"/>: a configured <see cref="GameCalendar"/> read against the host's <see cref="TimeProvider"/>.</summary>
public sealed class CalendarService : ICalendarService
{
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="calendar">The calendar all players share.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    public CalendarService(GameCalendar calendar, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        this.Calendar = calendar;
        _time = time;
    }

    /// <inheritdoc/>
    public GameCalendar Calendar { get; }

    /// <inheritdoc/>
    public long NowMs() => _time.GetUtcNow().ToUnixTimeMilliseconds();
}
