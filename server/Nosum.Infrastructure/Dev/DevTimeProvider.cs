// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Infrastructure.Dev;

/// <summary>
/// Development only: a <see cref="TimeProvider"/> that shows another clock plus an offset the dev tools can move forward (docs/adr/ADR-021 D7).
/// Every service that reads the time through it sees the jump at once. Timers and timestamps pass through unshifted, so a timer armed before a
/// jump has to be re-armed by whoever moved the clock (<see cref="DevClockService"/> does it for the day clock).
/// </summary>
public sealed class DevTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private long _offsetTicks;

    /// <summary>Initializes the provider.</summary>
    /// <param name="inner">The clock to shift: the system clock, or a fake one in tests.</param>
    public DevTimeProvider(TimeProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc/>
    public override long TimestampFrequency => _inner.TimestampFrequency;

    /// <summary>Gets how far the clock has been moved forward.</summary>
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">A positive span.</param>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(by, TimeSpan.Zero);
        _ = Interlocked.Add(ref _offsetTicks, by.Ticks);
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow() + this.Offset;

    /// <inheritdoc/>
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _inner.CreateTimer(callback, state, dueTime, period);
}
