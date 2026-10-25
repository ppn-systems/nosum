// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Nosum.Infrastructure.Persistence;

namespace Nosum.Infrastructure.Accounts;

/// <summary>
/// Deletes guest players that have not come back for <see cref="GuestRetention"/>, with their state and
/// session tokens (cascade), and sweeps stale token and recovery code rows. Registered players are never touched. Reusable: <see cref="RunOnceAsync"/>
/// does one pass, <see cref="RunAsync"/> repeats it on a timer until cancelled.
/// </summary>
public sealed class GuestCleanupService
{
    /// <summary>How long a guest may stay away before being deleted.</summary>
    public static readonly TimeSpan GuestRetention = TimeSpan.FromDays(30);

    /// <summary>How long an expired or revoked session token row is kept before it is deleted.</summary>
    public static readonly TimeSpan TokenRetention = TimeSpan.FromDays(7);

    /// <summary>How long an expired recovery code row is kept before it is deleted.</summary>
    public static readonly TimeSpan ResetCodeRetention = TimeSpan.FromDays(1);

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
            // States and tokens go with the player (ON DELETE CASCADE).
            int guests = await db.Players
                .Where(p => p.IsGuest && p.LastActiveAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // Session rotation leaves a revoked row behind at every resume, and recovery codes are single use: both are swept after a week and a day (docs/adr/ADR-032 D5).
            DateTimeOffset now = _time.GetUtcNow();
            DateTimeOffset tokenCutoff = now - TokenRetention;
            DateTimeOffset codeCutoff = now - ResetCodeRetention;
            _ = await db.PlayerSessionTokens
                .Where(t => t.ExpiresAt < tokenCutoff || (t.RevokedAt != null && t.RevokedAt < tokenCutoff))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            _ = await db.PasswordResetCodes
                .Where(c => c.ExpiresAt < codeCutoff)
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
