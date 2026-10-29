// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Gameplay.Emotes;

/// <summary>
/// The emote cooldown of one player (docs/adr/ADR-071 D2): one emote per <see cref="EmoteRules.MinIntervalMs"/>. The transport also throttles
/// the opcode with <c>[PacketRateLimit]</c>; this class holds the exact rule. Time is passed in so tests are deterministic. Thread safe.
/// </summary>
public sealed class EmoteCooldown
{
    private readonly Lock _gate = new();
    private long _lastMs;
    private bool _started;

    /// <summary>Takes the emote slot when the cooldown has passed.</summary>
    /// <param name="nowMs">The current time in milliseconds (any monotonic origin).</param>
    /// <returns><see langword="true"/> when the emote may be shown; <see langword="false"/> while the cooldown runs.</returns>
    public bool TryTake(long nowMs)
    {
        lock (_gate)
        {
            if (_started && Math.Max(0, nowMs - _lastMs) < EmoteRules.MinIntervalMs)
            {
                return false;
            }

            _started = true;
            _lastMs = nowMs;
            return true;
        }
    }
}
