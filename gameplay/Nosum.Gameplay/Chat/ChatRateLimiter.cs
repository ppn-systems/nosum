// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Gameplay.Chat;

/// <summary>
/// The per-player chat rate rule (docs/adr/ADR-029 D1): 5 messages per 10 seconds, with a burst of 3. A token bucket that holds
/// <see cref="Burst"/> tokens and refills <see cref="PerWindow"/> tokens every <see cref="WindowMs"/> milliseconds; each message takes one.
/// The transport also throttles the opcode with <c>[PacketRateLimit]</c>, which can only express whole requests per second; this class holds
/// the exact rule. Time is passed in so tests are deterministic. Thread safe.
/// </summary>
public sealed class ChatRateLimiter
{
    /// <summary>Messages allowed back to back.</summary>
    public const int Burst = 3;

    /// <summary>Messages allowed per <see cref="WindowMs"/> in the long run.</summary>
    public const int PerWindow = 5;

    /// <summary>The length of the window, in milliseconds.</summary>
    public const long WindowMs = 10_000;

    private readonly Lock _gate = new();
    private double _tokens = Burst;
    private long _lastMs;
    private bool _started;

    /// <summary>Takes one message from the bucket.</summary>
    /// <param name="nowMs">The current time in milliseconds (any monotonic origin).</param>
    /// <returns><see langword="true"/> when the message may be sent; <see langword="false"/> when the player is over the limit.</returns>
    public bool TryTake(long nowMs)
    {
        lock (_gate)
        {
            if (_started)
            {
                long elapsed = Math.Max(0, nowMs - _lastMs);
                _tokens = Math.Min(Burst, _tokens + (elapsed * PerWindow / (double)WindowMs));
            }

            _started = true;
            _lastMs = nowMs;
            if (_tokens < 1)
            {
                return false;
            }

            _tokens -= 1;
            return true;
        }
    }
}
