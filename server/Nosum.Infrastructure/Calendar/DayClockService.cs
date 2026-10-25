// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Nosum.Abstractions.Calendar;
using Nosum.Gameplay.Calendar;

namespace Nosum.Infrastructure.Calendar;

/// <summary>
/// <see cref="IDayClock"/>: a single timer aligned to the calendar's day boundaries. Each tick compares the day number now with the last one
/// it delivered and raises <see cref="DayEnded"/> for every day in between, so a late or coalesced tick, or a clock jump (the dev clock),
/// loses no day. The timer is re-armed from the clock after every tick; <see cref="Reconcile"/> re-arms it by hand after the clock was moved.
/// </summary>
public sealed class DayClockService : IDayClock, IDisposable
{
    // Fires a little after the boundary so a coarse timer never wakes just before it and has to re-arm for a few milliseconds.
    private const long BoundarySlackMs = 50;

    // A clock moved by years would otherwise raise thousands of events; the lazy state is right either way.
    private const int MaxEventsPerTick = 400;

    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly Action<Exception>? _onHandlerError;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private int _lastDay;
    private bool _disposed;

    /// <summary>Initializes the clock; call <see cref="Start"/> to arm it.</summary>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    /// <param name="onHandlerError">Told about an exception thrown by a subscriber; <see langword="null"/> ignores it.</param>
    public DayClockService(GameCalendar calendar, TimeProvider time, Action<Exception>? onHandlerError = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _calendar = calendar;
        _time = time;
        _onHandlerError = onHandlerError;
        _lastDay = calendar.DayNumberAt(time.GetUtcNow().ToUnixTimeMilliseconds());
    }

    /// <inheritdoc/>
    public event Action<GameDayEnded>? DayEnded;

    /// <inheritdoc/>
    public int CurrentDay => _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());

    /// <summary>Arms the timer for the next day boundary. Days that ended before this call are not reported.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _lastDay = this.CurrentDay;
            _timer ??= _time.CreateTimer(_ => this.Reconcile(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            this.ArmLocked();
        }
    }

    /// <summary>
    /// Raises <see cref="DayEnded"/> for every day that ended since the last call and re-arms the timer. The timer calls it; so does the dev clock
    /// after it moved the time (a timer measures real intervals and does not notice a jump).
    /// </summary>
    public void Reconcile()
    {
        int from;
        int to;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            to = this.CurrentDay;
            from = _lastDay;
            _lastDay = Math.Max(_lastDay, to);
            this.ArmLocked();
        }

        // Raised outside the lock so a subscriber may call back into the clock.
        int first = Math.Max(from, to - MaxEventsPerTick);
        for (int day = first; day < to; day++)
        {
            this.Raise(new GameDayEnded(day, _calendar.DayStartMs(day + 1)));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void ArmLocked()
    {
        if (_timer is null)
        {
            return;
        }

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        long dueMs = Math.Max(1, _calendar.NextDayStartMs(nowMs) - nowMs + BoundarySlackMs);
        _ = _timer.Change(TimeSpan.FromMilliseconds(dueMs), Timeout.InfiniteTimeSpan);
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failing subscriber must not stop the day clock or the other subscribers.")]
    private void Raise(GameDayEnded ended)
    {
        Action<GameDayEnded>? handlers = this.DayEnded;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<GameDayEnded>)handler)(ended);
            }
            catch (Exception ex)
            {
                _onHandlerError?.Invoke(ex);
            }
        }
    }
}
