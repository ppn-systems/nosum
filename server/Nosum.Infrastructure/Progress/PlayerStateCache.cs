// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Combat;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Progress;

/// <summary>
/// The in-memory copy of a player's <c>player_states</c> row, shared by the two services that write it (docs/adr/ADR-035 D4): <c>PlayerStateService</c> owns the saved
/// place, <c>PlayerProgressService</c> owns energy and experience. A join, a map change and a swing read the same entry, so the row is read once per signed-in player.
/// Writers write only their own columns (<see cref="IRepository{TEntity}.Update(TEntity, string[])"/>) and move the entry only after their save succeeded;
/// anything unclear drops it and the next call reads the row again.
/// </summary>
public sealed class PlayerStateCache
{
    private const long IdleLimitMilliseconds = 30 * 60 * 1000;

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private int _operations;

    /// <summary>Gets the per-player lock the progress service holds for a charge (docs/adr/ADR-035 D4: energy and experience are serialized; inventory first, then this one).</summary>
    internal StripedAsyncLock Locks { get; } = new(64);

    /// <summary>Describes the copy held for a player without loading it, for the audit of the caches.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The copy as text, or <see langword="null"/> when none is held.</returns>
    public string? DescribeHeld(Guid playerId) => _entries.TryGetValue(playerId, out Entry? entry) ? Describe(entry.Current) : null;

    /// <summary>Reads the row of a player and describes it (use a cache that holds nothing).</summary>
    /// <param name="units">Creates the unit of work for the read.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row as text.</returns>
    public async ValueTask<string> DescribeAsync(IUnitOfWorkFactory units, Guid playerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        return Describe(await this.GetAsync(units, playerId, cancellationToken).ConfigureAwait(false));
    }

    private static string Describe(Snapshot s)
        => $"exists={s.Exists};place={s.MapName}@{s.X},{s.Y},{s.Facing};energy={s.Energy}@{s.EnergyRefilledDay};xp={s.FarmingXp},{s.ForagingXp},{s.MiningXp},{s.FishingXp}";

    /// <summary>Lets go of a player's copy (left the game, signed out, or a save of unknown outcome); the next call reads the row.</summary>
    /// <param name="playerId">The player.</param>
    public void Forget(Guid playerId) => _ = _entries.TryRemove(playerId, out _);

    /// <summary>Reads the player's row from memory, or from the database once.</summary>
    /// <param name="units">Creates the unit of work for the read.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot; <see cref="Snapshot.Exists"/> is false for a player without a row.</returns>
    internal async ValueTask<Snapshot> GetAsync(IUnitOfWorkFactory units, Guid playerId, CancellationToken cancellationToken)
    {
        this.Trim();
        if (_entries.TryGetValue(playerId, out Entry? entry))
        {
            entry.Touch();
            return entry.Current;
        }

        Snapshot read = await ReadRowAsync(units, playerId, cancellationToken).ConfigureAwait(false);

        // Whoever stored one first wins; a concurrent writer's newer value is not overwritten by this older read.
        return _entries.GetOrAdd(playerId, _ => new Entry(read)).Current;
    }

    /// <summary>Looks at the copy held for a player without loading it or refreshing its last use.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="snapshot">The copy, when one is held.</param>
    /// <returns>Whether a copy is held.</returns>
    internal bool TryPeek(Guid playerId, out Snapshot snapshot)
    {
        if (_entries.TryGetValue(playerId, out Entry? entry))
        {
            snapshot = entry.Current;
            return true;
        }

        snapshot = Snapshot.Missing;
        return false;
    }

    /// <summary>Reads the player's row from the database without keeping a copy of it.</summary>
    /// <param name="units">Creates the unit of work for the read.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot; <see cref="Snapshot.Exists"/> is false for a player without a row.</returns>
    internal static async ValueTask<Snapshot> ReadRowAsync(IUnitOfWorkFactory units, Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = units.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerState? row = await unit.Repository<PlayerState>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            return row is null
                ? Snapshot.Missing
                : new Snapshot(
                    true,
                    row.MapName,
                    row.X,
                    row.Y,
                    row.Facing,
                    row.Energy,
                    row.EnergyRefilledDay,
                    row.FarmingXp,
                    row.ForagingXp,
                    row.MiningXp,
                    row.UpdatedAt,
                    row.CombatXp,
                    row.Hp > 0 ? row.Hp : CombatRules.BaseMaxHp,
                    row.Mp,
                    row.FishingXp);
        }
    }

    /// <summary>Applies a saved change to the copy, if there is one (a missing copy is simply read again later).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="change">Maps the current snapshot to the saved one; may run more than once.</param>
    internal void Apply(Guid playerId, Func<Snapshot, Snapshot> change)
    {
        if (_entries.TryGetValue(playerId, out Entry? entry))
        {
            entry.Replace(change);
        }
    }

    private void Trim()
    {
        if ((Interlocked.Increment(ref _operations) & 0xFF) != 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        foreach (KeyValuePair<Guid, Entry> pair in _entries.Where(p => now - p.Value.LastUsed > IdleLimitMilliseconds).ToList())
        {
            _ = _entries.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>The columns of the row; immutable.</summary>
    /// <param name="Exists">Whether the database has a row.</param>
    /// <param name="MapName">The saved map; empty for "no saved place".</param>
    /// <param name="X">Saved x.</param>
    /// <param name="Y">Saved y.</param>
    /// <param name="Facing">Saved facing.</param>
    /// <param name="Energy">Energy at the last refill day.</param>
    /// <param name="EnergyRefilledDay">The game day the energy was last refilled on.</param>
    /// <param name="FarmingXp">Farming experience.</param>
    /// <param name="ForagingXp">Foraging experience.</param>
    /// <param name="MiningXp">Mining experience.</param>
    /// <param name="UpdatedAt">When the row was last written.</param>
    /// <param name="CombatXp">Combat experience.</param>
    /// <param name="Hp">Current health points.</param>
    /// <param name="Mp">Current mana points.</param>
    /// <param name="FishingXp">Fishing experience.</param>
    internal sealed record Snapshot(
        bool Exists,
        string MapName,
        double X,
        double Y,
        Facing Facing,
        int Energy,
        long EnergyRefilledDay,
        int FarmingXp,
        int ForagingXp,
        int MiningXp,
        DateTimeOffset UpdatedAt,
        int CombatXp = 0,
        int Hp = CombatRules.BaseMaxHp,
        int Mp = CombatRules.BaseMaxMp,
        int FishingXp = 0)
    {
        /// <summary>Gets the snapshot of a player with no row, with the defaults a new row gets.</summary>
        public static Snapshot Missing { get; } = new(false, string.Empty, 0, 0, default, 0, -1, 0, 0, 0, default, 0, CombatRules.BaseMaxHp, CombatRules.BaseMaxMp, 0);
    }

    private sealed class Entry
    {
        private Snapshot _current;
        private long _lastUsed = Environment.TickCount64;

        public Entry(Snapshot current) => _current = current;

        public Snapshot Current => Volatile.Read(ref _current);

        public long LastUsed => Volatile.Read(ref _lastUsed);

        public void Touch() => Volatile.Write(ref _lastUsed, Environment.TickCount64);

        public void Replace(Func<Snapshot, Snapshot> change)
        {
            Snapshot seen;
            Snapshot next;
            do
            {
                seen = Volatile.Read(ref _current);
                next = change(seen);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _current, next, seen), seen));
        }
    }
}
