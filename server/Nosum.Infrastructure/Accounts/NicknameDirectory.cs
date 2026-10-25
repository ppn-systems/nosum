// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Accounts;

/// <summary><see cref="INicknameDirectory"/> over the players table with an in-memory cache.</summary>
public sealed class NicknameDirectory : INicknameDirectory
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ConcurrentDictionary<Guid, string> _cache = new();

    /// <summary>Initializes the directory.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    public NicknameDirectory(IUnitOfWorkFactory unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public async ValueTask<string> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<Guid, string> found = await this.GetManyAsync([playerId], cancellationToken).ConfigureAwait(false);
        return found.TryGetValue(playerId, out string? nickname) ? nickname : string.Empty;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, string>> GetManyAsync(IEnumerable<Guid> playerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        Dictionary<Guid, string> result = [];
        List<Guid> missing = [];
        foreach (Guid id in playerIds.Distinct())
        {
            if (_cache.TryGetValue(id, out string? cached))
            {
                result[id] = cached;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                List<Player> players = await unit.Repository<Player>().ListAsync(p => missing.Contains(p.Id), cancellationToken).ConfigureAwait(false);
                foreach (Player player in players)
                {
                    _cache[player.Id] = player.Nickname;
                    result[player.Id] = player.Nickname;
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public void Remember(Guid playerId, string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _cache[playerId] = nickname;
    }
}
