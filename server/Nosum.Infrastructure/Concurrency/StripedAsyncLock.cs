// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Infrastructure.Concurrency;

/// <summary>
/// A fixed set of async locks chosen by key hash: callers with the same key never run together,
/// callers with different keys rarely wait for each other, and nothing is allocated per key.
/// In-process only (the server is a single process, docs/adr/ADR-009).
/// </summary>
internal sealed class StripedAsyncLock
{
    private readonly SemaphoreSlim[] _locks;

    public StripedAsyncLock(int stripes = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stripes);
        _locks = new SemaphoreSlim[stripes];
        for (int i = 0; i < _locks.Length; i++)
        {
            _locks[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>Waits for the key's lock; dispose the result to release it.</summary>
    public async ValueTask<Releaser> EnterAsync<TKey>(TKey key, CancellationToken cancellationToken)
        where TKey : notnull
    {
        SemaphoreSlim gate = _locks[(uint)key.GetHashCode() % (uint)_locks.Length];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    /// <summary>
    /// Waits for the locks of several keys at once, always in stripe order so two callers with overlapping keys cannot deadlock,
    /// and takes a stripe only once when several keys share it. Dispose the result to release them all.
    /// </summary>
    public async ValueTask<IDisposable> EnterAllAsync<TKey>(IReadOnlyCollection<TKey> keys, CancellationToken cancellationToken)
        where TKey : notnull
    {
        SortedSet<int> stripes = [];
        foreach (TKey key in keys)
        {
            _ = stripes.Add((int)((uint)key.GetHashCode() % (uint)_locks.Length));
        }

        List<SemaphoreSlim> held = new(stripes.Count);
        try
        {
            foreach (int stripe in stripes)
            {
                await _locks[stripe].WaitAsync(cancellationToken).ConfigureAwait(false);
                held.Add(_locks[stripe]);
            }
        }
        catch
        {
            new MultiReleaser(held).Dispose();
            throw;
        }

        return new MultiReleaser(held);
    }

    private sealed class MultiReleaser : IDisposable
    {
        private readonly List<SemaphoreSlim> _held;

        public MultiReleaser(List<SemaphoreSlim> held) => _held = held;

        public void Dispose()
        {
            foreach (SemaphoreSlim gate in _held)
            {
                _ = gate.Release();
            }

            _held.Clear();
        }
    }

    public readonly struct Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => _ = _gate.Release();
    }
}
