// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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

/// <summary>Tells the node service when a game day ended (docs/adr/ADR-021 D4): the server calendar has no event for it, so this job watches <see cref="IGameDayClock"/>.</summary>
public sealed class NodeDayService
{
    private readonly IResourceNodeService _nodes;
    private readonly IGameDayClock _clock;
    private readonly TimeProvider _time;

    /// <summary>Initializes the job.</summary>
    /// <param name="nodes">The node service.</param>
    /// <param name="clock">The game day count.</param>
    /// <param name="time">The clock the job's timer runs on.</param>
    public NodeDayService(IResourceNodeService nodes, IGameDayClock clock, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(time);

        _nodes = nodes;
        _clock = clock;
        _time = time;
    }

    /// <summary>Checks the day a few times per game day (at most once a minute) and advances the nodes when it changed, until cancelled.</summary>
    /// <param name="onError">Called with a failure; the job goes on.</param>
    /// <param name="cancellationToken">Stops the job.</param>
    /// <returns>A task that completes when the job stopped.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "One failed rollover must not stop the job; the next tick retries the same day.")]
    public async Task RunAsync(Action<Exception> onError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onError);

        long lastDay = _clock.Today;
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(Math.Clamp(_clock.DayLength.TotalMilliseconds / 4, 250, 60_000)), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                long today = _clock.Today;
                if (today == lastDay)
                {
                    continue;
                }

                try
                {
                    await _nodes.AdvanceDayAsync(today, today / GameCalendar.DaysPerSeason > lastDay / GameCalendar.DaysPerSeason, cancellationToken).ConfigureAwait(false);
                    lastDay = today;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onError(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
