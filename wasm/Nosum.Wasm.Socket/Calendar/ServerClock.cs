// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Wasm.Socket.Calendar;

/// <summary>
/// The server's clock as the client estimates it (<see cref="CalendarChannel.ServerNowMs"/>). Client timers that mirror a server timer (the furnace batch,
/// docs/adr/ADR-024 D4) count in this clock, so they move with the server's game clock, dev clock jumps included, instead of the local system clock.
/// Timers pass through to the system clock; whoever waits on one re-reads this clock when it wakes.
/// </summary>
public sealed class ServerClock : TimeProvider
{
    private readonly CalendarChannel _calendar;

    /// <summary>Initializes the clock.</summary>
    /// <param name="calendar">The calendar channel whose server clock estimate this shows.</param>
    public ServerClock(CalendarChannel calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        _calendar = calendar;
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(_calendar.ServerNowMs());

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        TimeProvider.System.CreateTimer(callback, state, dueTime, period);
}
