// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Nosum.Abstractions.Security;

namespace Nosum.Infrastructure.Security;

/// <summary>
/// In-memory <see cref="IAbuseGuard"/>: a list of event times per bucket and key, pruned as it is read. Memory is bounded: when the table
/// grows past <see cref="MaxTrackedKeys"/>, keys whose events are all older than the longest window seen are dropped.
/// </summary>
public sealed class SlidingWindowAbuseGuard : IAbuseGuard
{
    /// <summary>Most distinct keys tracked before a sweep drops the idle ones.</summary>
    public const int MaxTrackedKeys = 50_000;

    private readonly TimeProvider _time;
    private readonly bool _exemptLoopback;
    private readonly ConcurrentDictionary<(string Bucket, string Key), Entry> _entries = new();
    private long _longestWindowTicks;

    /// <summary>Initializes the guard.</summary>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="exemptLoopback">Whether keys that are loopback addresses are never limited (local development and tests; behind a reverse proxy on the same host, forward the real client address or turn this off).</param>
    public SlidingWindowAbuseGuard(TimeProvider time, bool exemptLoopback = false)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _exemptLoopback = exemptLoopback;
    }

    /// <summary>Gets how many keys are tracked (for tests and metrics).</summary>
    public int TrackedKeys => _entries.Count;

    /// <inheritdoc/>
    public bool TryAcquire(string bucket, string key, int limit, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (_exemptLoopback && System.Net.IPAddress.TryParse(key, out System.Net.IPAddress? address) && System.Net.IPAddress.IsLoopback(address))
        {
            return true;
        }

        long now = _time.GetTimestamp();
        long windowTicks = (long)(window.TotalSeconds * _time.TimestampFrequency);
        if (windowTicks > Interlocked.Read(ref _longestWindowTicks))
        {
            _ = Interlocked.Exchange(ref _longestWindowTicks, windowTicks);
        }

        if (_entries.Count > MaxTrackedKeys)
        {
            this.Sweep(now);
        }

        Entry entry = _entries.GetOrAdd((bucket, key), static _ => new Entry());
        lock (entry.Gate)
        {
            while (entry.Times.Count > 0 && now - entry.Times.Peek() >= windowTicks)
            {
                _ = entry.Times.Dequeue();
            }

            if (entry.Times.Count >= limit)
            {
                return false;
            }

            entry.Times.Enqueue(now);
            entry.Last = now;
            return true;
        }
    }

    private void Sweep(long now)
    {
        long longest = Interlocked.Read(ref _longestWindowTicks);
        foreach (KeyValuePair<(string Bucket, string Key), Entry> pair in _entries)
        {
            lock (pair.Value.Gate)
            {
                if (pair.Value.Times.Count == 0 || now - pair.Value.Last >= longest)
                {
                    _ = _entries.TryRemove(pair);
                }
            }
        }
    }

    private sealed class Entry
    {
        public Lock Gate { get; } = new();

        public Queue<long> Times { get; } = new();

        public long Last { get; set; }
    }
}
