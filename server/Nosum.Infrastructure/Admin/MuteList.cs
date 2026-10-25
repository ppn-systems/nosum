// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Admin;
using Nosum.Abstractions.Data;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Admin;

/// <summary>
/// <see cref="IMuteList"/> in memory (docs/adr/ADR-031 D7): the deployment runs one server process, so the list is the truth for chat and the
/// database column <c>muted_until</c> is how it survives a restart. <see cref="AdminService"/> keeps both in step.
/// </summary>
public sealed class MuteList : IMuteList
{
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _until = new();

    /// <summary>Initializes an empty list.</summary>
    /// <param name="time">The clock a mute ends by.</param>
    public MuteList(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc/>
    public bool IsMuted(Guid playerId)
    {
        if (!_until.TryGetValue(playerId, out DateTimeOffset until))
        {
            return false;
        }

        if (until > _time.GetUtcNow())
        {
            return true;
        }

        _ = _until.TryRemove(playerId, out _);
        return false;
    }

    /// <summary>Mutes a player until a time, or lifts the mute when the time is not in the future.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="until">When the mute ends.</param>
    public void Set(Guid playerId, DateTimeOffset until)
    {
        if (until > _time.GetUtcNow())
        {
            _until[playerId] = until;
        }
        else
        {
            _ = _until.TryRemove(playerId, out _);
        }
    }

    /// <summary>Loads every mute that is still running from the database; called once at start.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the list is filled.</returns>
    public async Task LoadAsync(IUnitOfWorkFactory unitOfWork, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            foreach (Player player in await unit.Repository<Player>().ListAsync(p => p.MutedUntil > now, cancellationToken).ConfigureAwait(false))
            {
                this.Set(player.Id, player.MutedUntil!.Value);
            }
        }
    }
}
