// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Nodes;
using Nosum.Gameplay.Calendar;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// <see cref="IGameDayClock"/> on the season calendar every player shares (docs/adr/ADR-018): a game day is <see cref="GameCalendar.DayLengthMs"/> of real time counted
/// from the calendar's epoch; before the epoch the day is zero.
/// </summary>
public sealed class CalendarGameDayClock : IGameDayClock
{
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;

    /// <summary>Initializes the clock.</summary>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="time">The clock days are counted on.</param>
    public CalendarGameDayClock(GameCalendar calendar, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _calendar = calendar;
        _time = time;
    }

    /// <inheritdoc/>
    public long Today => Math.Max(0, _time.GetUtcNow().ToUnixTimeMilliseconds() - _calendar.EpochMs) / _calendar.DayLengthMs;

    /// <inheritdoc/>
    public TimeSpan DayLength => TimeSpan.FromMilliseconds(_calendar.DayLengthMs);
}
