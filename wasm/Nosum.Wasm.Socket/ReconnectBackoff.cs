// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.Socket;

/// <summary>The wait before each reconnect attempt: the base delay, doubled for every failed attempt, never longer than the maximum.</summary>
public static class ReconnectBackoff
{
    /// <summary>Gets the wait before the next attempt after <paramref name="failures"/> failed attempts; the first retry waits the base delay.</summary>
    /// <param name="failures">Failed attempts so far. 0 and 1 both give the base delay.</param>
    /// <param name="baseDelay">The first delay.</param>
    /// <param name="maxDelay">The longest delay.</param>
    /// <returns>The delay.</returns>
    public static TimeSpan Delay(int failures, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        double factor = Math.Pow(2, Math.Min(Math.Max(failures - 1, 0), 10));
        return TimeSpan.FromMilliseconds(Math.Min(baseDelay.TotalMilliseconds * factor, maxDelay.TotalMilliseconds));
    }
}
