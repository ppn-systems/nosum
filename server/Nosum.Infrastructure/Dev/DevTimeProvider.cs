// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Infrastructure.Dev;

/// <summary>
/// Development only: a <see cref="TimeProvider"/> that shows another clock plus an offset the dev tools can move forward (docs/adr/ADR-021 D7).
/// Every service that reads the time through it sees the jump at once. Timers and timestamps pass through unshifted, so a timer armed before a
/// jump has to be re-armed by whoever moved the clock (<see cref="DevClockService"/> does it for the day clock). A frozen clock holds its instant
/// until it is resumed; the time it was held is then not counted (docs/adr/ADR-079 amendment 2026-11-01).
/// </summary>
public sealed class DevTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private long _offsetTicks;

    // The UTC ticks the clock is pinned at while frozen; zero while the clock runs.
    private long _frozenUtcTicks;

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

    /// <summary>Gets whether the clock is pinned (docs/adr/ADR-079 amendment 2026-11-01).</summary>
    public bool IsFrozen => Interlocked.Read(ref _frozenUtcTicks) != 0;

    /// <summary>Pins the clock at this instant: it stops moving until <see cref="Resume"/>. A pinned clock still moves when <see cref="Advance"/> is called.</summary>
    public void Freeze()
    {
        if (!this.IsFrozen)
        {
            _ = Interlocked.CompareExchange(ref _frozenUtcTicks, _inner.GetUtcNow().UtcTicks, 0);
        }
    }

    /// <summary>Lets a pinned clock run again from the instant it was pinned at: the time it was held does not count as elapsed.</summary>
    public void Resume()
    {
        long frozen = Interlocked.Exchange(ref _frozenUtcTicks, 0);
        if (frozen != 0)
        {
            long held = _inner.GetUtcNow().UtcTicks - frozen;
            _ = Interlocked.Add(ref _offsetTicks, -held);
        }
    }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        long frozen = Interlocked.Read(ref _frozenUtcTicks);
        DateTimeOffset now = frozen == 0 ? _inner.GetUtcNow() : new DateTimeOffset(frozen, TimeSpan.Zero);
        return now + this.Offset;
    }

    /// <inheritdoc/>
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _inner.CreateTimer(callback, state, dueTime, period);
}
