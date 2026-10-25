// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Community;
using Nosum.Abstractions.Data;
using Nosum.Domain.Community;

namespace Nosum.Infrastructure.Community;

/// <summary>
/// <see cref="ICommunityUnlocks"/>: the flags in memory, loaded from the <c>community_unlocks</c> table the first time anything asks (or by <see cref="LoadAsync"/>)
/// and kept current by <see cref="Add"/>, which the goal service calls when a completed goal's transaction committed. The server is one process
/// (docs/adr/ADR-009), so the set cannot go stale.
/// </summary>
public sealed class CommunityUnlocks : ICommunityUnlocks
{
    private readonly IUnitOfWorkFactory _units;
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);
    private readonly Lock _loadGate = new();
    private volatile bool _loaded;

    /// <summary>Initializes the flags.</summary>
    /// <param name="units">Creates units of work.</param>
    public CommunityUnlocks(IUnitOfWorkFactory units)
    {
        ArgumentNullException.ThrowIfNull(units);

        _units = units;
    }

    /// <summary>Loads the flags from the database. Safe to call again; called by the host at start so the first question does not wait.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the flags are in memory.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<CommunityUnlockRecord> rows = await unit.Repository<CommunityUnlockRecord>().ListAsync(_ => true, cancellationToken).ConfigureAwait(false);
            foreach (CommunityUnlockRecord row in rows)
            {
                _ = _keys.TryAdd(row.Key, 0);
            }
        }

        _loaded = true;
    }

    /// <inheritdoc/>
    public bool IsUnlocked(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        this.EnsureLoaded();
        return _keys.ContainsKey(key);
    }

    /// <summary>Records that a flag is set (its row was committed).</summary>
    /// <param name="key">The unlock key.</param>
    internal void Add(string key) => _ = _keys.TryAdd(key, 0);

    // The first question loads the table once; a blocking wait is fine here, it is a single small query on a thread-pool thread.
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_loadGate)
        {
            if (!_loaded)
            {
                Task.Run(() => this.LoadAsync()).GetAwaiter().GetResult();
            }
        }
    }
}
