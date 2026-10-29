// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.Socket;

/// <summary>
/// Decides how long the socket waits before each reconnect attempt (issues #150 and #151). The delay doubles for every failed attempt and is capped,
/// and a random spread of ±25% keeps clients that lost the server together from retrying together. Only a session that stayed up for
/// <see cref="StableAfter"/> counts as a success: one that closes sooner, or a handshake the server refuses, keeps the backoff growing.
/// </summary>
/// <remarks>
/// Time comes from <see cref="TimeProvider"/> and the spread from a unit-interval source, so tests pin both. The delay the socket announces
/// (<c>ReconnectScheduled</c>) is the delay it waits, so the reconnect overlay's countdown matches.
/// </remarks>
public sealed class ReconnectSchedule
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly TimeProvider _time;
    private readonly Func<double> _unitRandom;
    private DateTimeOffset? _connectedAt;
    private int _failures;

    /// <summary>Initializes the schedule.</summary>
    /// <param name="baseDelay">The first delay after a failure.</param>
    /// <param name="maxDelay">The longest delay before the spread is applied; the longest actual wait is 1.25 times this.</param>
    /// <param name="stableAfter">How long a session must stay up to reset the backoff.</param>
    /// <param name="time">The clock; the system clock when <see langword="null"/>.</param>
    /// <param name="unitRandom">Returns a number in [0, 1) for the spread; <see cref="Random.Shared"/> when <see langword="null"/>.</param>
    public ReconnectSchedule(TimeSpan baseDelay, TimeSpan maxDelay, TimeSpan stableAfter, TimeProvider? time = null, Func<double>? unitRandom = null)
    {
        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
        StableAfter = stableAfter;
        _time = time ?? TimeProvider.System;
        _unitRandom = unitRandom ?? Random.Shared.NextDouble;
    }

    /// <summary>Gets how long a session must stay up to count as stable.</summary>
    public TimeSpan StableAfter { get; }

    /// <summary>Gets the failed attempts since the last stable session; 0 before any failure.</summary>
    public int Failures => _failures;

    /// <summary>Records that the handshake succeeded. Does not reset the backoff: the session has to prove it stays up.</summary>
    public void Connected() => _connectedAt = _time.GetUtcNow();

    /// <summary>Records that a session closed. Resets the backoff only when it had stayed up for <see cref="StableAfter"/>.</summary>
    public void Lost()
    {
        bool stable = _connectedAt is { } at && _time.GetUtcNow() - at >= this.StableAfter;
        _connectedAt = null;
        _failures = stable ? 1 : _failures + 1;
    }

    /// <summary>Records an attempt that failed before a session was ready (DNS, refused, handshake, key check).</summary>
    public void Failed()
    {
        _connectedAt = null;
        _failures++;
    }

    /// <summary>Gets the wait before the next attempt: exponential from the base delay, capped, with the ±25% spread applied.</summary>
    /// <returns>The delay.</returns>
    public TimeSpan NextDelay()
    {
        TimeSpan capped = ReconnectBackoff.Delay(_failures, _baseDelay, _maxDelay);
        double spread = 0.75 + (_unitRandom() * 0.5);
        return TimeSpan.FromMilliseconds(capped.TotalMilliseconds * spread);
    }
}
