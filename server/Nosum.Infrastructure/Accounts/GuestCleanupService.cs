// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Nosum.Infrastructure.Persistence;

namespace Nosum.Infrastructure.Accounts;

/// <summary>
/// Deletes guest players that have not come back for <see cref="GuestRetention"/>, with their state and
/// session tokens (cascade), and sweeps stale session token rows. Registered players are never touched. Reusable: <see cref="RunOnceAsync"/>
/// does one pass, <see cref="RunAsync"/> repeats it on a timer until cancelled.
/// </summary>
public sealed class GuestCleanupService
{
    /// <summary>How long a guest may stay away before being deleted.</summary>
    public static readonly TimeSpan GuestRetention = TimeSpan.FromDays(30);

    /// <summary>How long an expired or revoked session token row is kept before it is deleted.</summary>
    public static readonly TimeSpan TokenRetention = TimeSpan.FromDays(7);

    /// <summary>How often <see cref="RunAsync"/> looks for idle guests.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    private readonly DbContextOptions<NosumDbContext> _options;
    private readonly TimeProvider _time;
    private readonly TimeSpan _retention;

    /// <summary>Initializes the service.</summary>
    /// <param name="options">Options of the database.</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="retention">Idle time after which a guest is deleted; defaults to <see cref="GuestRetention"/>.</param>
    public GuestCleanupService(DbContextOptions<NosumDbContext> options, TimeProvider time, TimeSpan? retention = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _options = options;
        _time = time;
        _retention = retention ?? GuestRetention;
    }

    /// <summary>Deletes every guest idle for longer than the retention time.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The number of guests deleted.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = _time.GetUtcNow() - _retention;
        NosumDbContext db = new(_options);
        await using (db.ConfigureAwait(false))
        {
            List<Guid> guestIds = await db.Players
                .Where(p => p.IsGuest && p.LastActiveAt < cutoff && !db.PlayerStates.Any(s => s.PlayerId == p.Id && s.UpdatedAt >= cutoff))
                .Select(p => p.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            int guests = 0;
            if (guestIds.Count > 0)
            {
                _ = await db.FarmPlots.Where(f => guestIds.Contains(f.OwnerId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.PlacedObjects.Where(o => guestIds.Contains(o.OwnerId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.WorldNodes.Where(n => guestIds.Contains(n.OwnerId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.NodeInstances.Where(n => guestIds.Contains(n.OwnerId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.GroundItems.Where(g => guestIds.Contains(g.OwnerId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.BlockedPlayers.Where(b => guestIds.Contains(b.OwnerId) || guestIds.Contains(b.BlockedId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.Friendships.Where(f => guestIds.Contains(f.PlayerId) || guestIds.Contains(f.FriendId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                _ = await db.FriendRequests.Where(r => guestIds.Contains(r.FromId) || guestIds.Contains(r.ToId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

                // States and tokens go with the player (ON DELETE CASCADE).
                guests = await db.Players
                    .Where(p => guestIds.Contains(p.Id))
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            // Session rotation leaves a revoked row behind at every resume; such rows are swept after a week (docs/adr/ADR-032 D5).
            DateTimeOffset now = _time.GetUtcNow();
            DateTimeOffset tokenCutoff = now - TokenRetention;
            _ = await db.PlayerSessionTokens
                .Where(t => t.ExpiresAt < tokenCutoff || (t.RevokedAt != null && t.RevokedAt < tokenCutoff))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return guests;
        }
    }

    /// <summary>Runs <see cref="RunOnceAsync"/> now and then every <paramref name="interval"/> until cancelled.</summary>
    /// <param name="interval">Time between passes.</param>
    /// <param name="onError">Called with a failure of a pass; the loop keeps going (the database may be briefly down).</param>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when cancelled.</returns>
    public async Task RunAsync(TimeSpan interval, Action<Exception> onError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onError);
        using PeriodicTimer timer = new(interval, _time);
        try
        {
            do
            {
                try
                {
                    _ = await this.RunOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
                {
                    onError(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
