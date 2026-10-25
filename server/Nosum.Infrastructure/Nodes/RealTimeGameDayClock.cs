// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Nodes;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// The stand-in game calendar until the seasons exist (docs/adr/ADR-018): a game day is a real UTC day. <see cref="NodeDayService"/> calls
/// <see cref="IResourceNodeService.AdvanceDayAsync"/> at each midnight.
/// </summary>
public sealed class RealTimeGameDayClock : IGameDayClock
{
    private readonly TimeProvider _time;

    /// <summary>Initializes the clock.</summary>
    /// <param name="time">The clock days are counted on.</param>
    public RealTimeGameDayClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc/>
    public long Today => _time.GetUtcNow().ToUnixTimeSeconds() / 86_400;
}

/// <summary>Tells the node service when a game day ended, using <see cref="IGameDayClock"/>; the seasons calendar replaces it by calling the service itself.</summary>
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

    /// <summary>Checks the day once a minute and advances the nodes when it changed, until cancelled.</summary>
    /// <param name="onError">Called with a failure; the job goes on.</param>
    /// <param name="cancellationToken">Stops the job.</param>
    /// <returns>A task that completes when the job stopped.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "One failed rollover must not stop the job; the next tick retries the same day.")]
    public async Task RunAsync(Action<Exception> onError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onError);

        long lastDay = _clock.Today;
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1), _time);
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
                    await _nodes.AdvanceDayAsync(today, false, cancellationToken).ConfigureAwait(false);
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
