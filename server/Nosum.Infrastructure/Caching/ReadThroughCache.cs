// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Infrastructure.Caching;

/// <summary>
/// A read-through cache with explicit invalidation (docs/adr/ADR-035 D5): the service that is the only writer of a table drops or replaces the entry after its save
/// succeeded, and the next reader loads it again. A load that was in flight while an entry was changed is used once and not kept (the epoch), so a reader that started
/// before a change never stores what it read before it. Entries that are not used for half an hour are swept. With <c>enabled: false</c> nothing is kept and every
/// read loads, which is how a service built by hand in a test runs uncached.
/// </summary>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TValue">The cached value; treat it as immutable.</typeparam>
internal sealed class ReadThroughCache<TKey, TValue>
    where TKey : notnull
{
    private const long IdleLimitMilliseconds = 30 * 60 * 1000;

    private readonly bool _enabled;
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();
    private long _epoch;
    private int _operations;

    public ReadThroughCache(bool enabled = true) => _enabled = enabled;

    /// <summary>Gets the number of entries held.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the value, loading it when it is not held.</summary>
    /// <param name="key">The key.</param>
    /// <param name="load">Reads the value from the database.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The value.</returns>
    /// <param name="alsoKeep">Names other keys the loaded value is also the value of (a co-op loaded for one member is the co-op of every member); they are kept under the same condition, without a read of their own.</param>
    public async ValueTask<TValue> GetAsync(TKey key, Func<CancellationToken, Task<TValue>> load, CancellationToken cancellationToken, Func<TValue, IEnumerable<TKey>>? alsoKeep = null)
    {
        if (!_enabled)
        {
            return await load(cancellationToken).ConfigureAwait(false);
        }

        this.Trim();
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            entry.Touch();
            return entry.Value;
        }

        long epoch = Volatile.Read(ref _epoch);
        TValue loaded = await load(cancellationToken).ConfigureAwait(false);
        if (Volatile.Read(ref _epoch) == epoch)
        {
            _entries[key] = new Entry(loaded);
            if (alsoKeep is not null)
            {
                foreach (TKey other in alsoKeep(loaded))
                {
                    _ = _entries.TryAdd(other, new Entry(loaded));
                }
            }
        }

        return loaded;
    }

    /// <summary>Reads the value if it is held, without loading.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value when held.</param>
    /// <returns>Whether it was held.</returns>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_enabled && _entries.TryGetValue(key, out Entry? entry))
        {
            entry.Touch();
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Drops an entry; a load that is in flight is not kept.</summary>
    /// <param name="key">The key.</param>
    public void Invalidate(TKey key)
    {
        _ = Interlocked.Increment(ref _epoch);
        _ = _entries.TryRemove(key, out _);
    }

    /// <summary>Replaces an entry with what the writer just saved; a load that is in flight is not kept.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The new value.</param>
    public void Set(TKey key, TValue value)
    {
        _ = Interlocked.Increment(ref _epoch);
        if (_enabled)
        {
            _entries[key] = new Entry(value);
        }
    }

    /// <summary>Changes a held entry; a missing entry stays missing (it is loaded fresh later).</summary>
    /// <param name="key">The key.</param>
    /// <param name="change">Maps the held value to the saved one.</param>
    public void Update(TKey key, Func<TValue, TValue> change)
    {
        _ = Interlocked.Increment(ref _epoch);
        while (_enabled && _entries.TryGetValue(key, out Entry? entry))
        {
            // Two writers changing the same entry never lose one another's change: the loser starts again from the winner's value.
            if (_entries.TryUpdate(key, new Entry(change(entry.Value)), entry))
            {
                return;
            }
        }
    }

    /// <summary>Drops every entry whose key matches.</summary>
    /// <param name="predicate">Chooses the keys to drop.</param>
    public void InvalidateWhere(Func<TKey, TValue, bool> predicate)
    {
        _ = Interlocked.Increment(ref _epoch);
        foreach (KeyValuePair<TKey, Entry> pair in _entries.Where(p => predicate(p.Key, p.Value.Value)).ToList())
        {
            _ = _entries.TryRemove(pair.Key, out _);
        }
    }

    private void Trim()
    {
        if ((Interlocked.Increment(ref _operations) & 0xFF) != 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        foreach (KeyValuePair<TKey, Entry> pair in _entries.Where(p => now - p.Value.LastUsed > IdleLimitMilliseconds).ToList())
        {
            _ = _entries.TryRemove(pair.Key, out _);
        }
    }

    private sealed class Entry
    {
        private long _lastUsed = Environment.TickCount64;

        public Entry(TValue value) => this.Value = value;

        public TValue Value { get; }

        public long LastUsed => Volatile.Read(ref _lastUsed);

        public void Touch() => Volatile.Write(ref _lastUsed, Environment.TickCount64);
    }
}
