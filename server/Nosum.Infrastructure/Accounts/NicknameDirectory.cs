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
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Caching;

namespace Nosum.Infrastructure.Accounts;

/// <summary>
/// <see cref="INicknameDirectory"/> over the players table with an in-memory cache. Besides the nickname of an id it keeps the facts the social services ask for
/// (guest or not, and the account behind a typed nickname), so they read no account row (docs/adr/ADR-035 D5). Every change of a nickname or of the guest flag
/// ends in <see cref="Remember"/> (<c>AccountService</c> is the only writer), which drops what was known about that player and about the new nickname's key.
/// </summary>
public sealed class NicknameDirectory : INicknameDirectory
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ConcurrentDictionary<Guid, string> _cache = new();
    private readonly ReadThroughCache<Guid, PlayerFact> _facts;
    private readonly ReadThroughCache<string, PlayerFact> _byKey;

    /// <summary>Initializes the directory.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="cacheFacts">Whether the facts of a player are kept; <see langword="false"/> reads the account row every time.</param>
    public NicknameDirectory(IUnitOfWorkFactory unitOfWork, bool cacheFacts = true)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _facts = new ReadThroughCache<Guid, PlayerFact>(cacheFacts);
        _byKey = new ReadThroughCache<string, PlayerFact>(cacheFacts);
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
        _facts.Invalidate(playerId);
        _byKey.InvalidateWhere((key, fact) => fact.Id == playerId || key == NicknameRules.Key(nickname));
    }

    /// <summary>Describes the facts held for a player without loading them, for the audit of the caches.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The facts as text, or <see langword="null"/> when none are held.</returns>
    public string? DescribeHeld(Guid playerId) => _facts.TryGet(playerId, out PlayerFact? fact) ? $"{fact.Id}:{fact.Nickname}:{fact.IsGuest}" : null;

    /// <summary>Reads the facts of a player and describes them (use a directory that keeps nothing).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The facts as text.</returns>
    public async ValueTask<string> DescribeAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerFact? fact = await this.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        return fact is null ? "none" : $"{fact.Id}:{fact.Nickname}:{fact.IsGuest}";
    }

    /// <inheritdoc/>
    public async ValueTask<PlayerFact?> FindAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerFact fact = await _facts.GetAsync(playerId, ct => this.LoadAsync(p => p.Id == playerId, ct), cancellationToken).ConfigureAwait(false);
        if (fact == s_missing)
        {
            _facts.Invalidate(playerId);
            return null;
        }

        _cache[playerId] = fact.Nickname;
        return fact;
    }

    /// <inheritdoc/>
    public async ValueTask<PlayerFact?> FindByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        PlayerFact fact = await _byKey.GetAsync(key, ct => this.LoadAsync(p => p.NormalizedNickname == key, ct), cancellationToken).ConfigureAwait(false);
        if (fact == s_missing)
        {
            // Not kept: a player who registers with this nickname later must be found.
            _byKey.Invalidate(key);
            return null;
        }

        _cache[fact.Id] = fact.Nickname;
        return fact;
    }

    private static readonly PlayerFact s_missing = new(Guid.Empty, string.Empty, true);

    private async Task<PlayerFact> LoadAsync(System.Linq.Expressions.Expression<Func<Player, bool>> match, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? player = await unit.Repository<Player>().FirstOrDefaultAsync(match, cancellationToken).ConfigureAwait(false);
            return player is null ? s_missing : new PlayerFact(player.Id, player.Nickname, player.IsGuest);
        }
    }
}
