// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Accounts;

/// <summary>
/// Resolves a player's current nickname from their account id, for everything the server sends that names a player (looks, market sellers,
/// co-op members). Cached in memory after the first read (single process, docs/adr/ADR-009); the account service updates it on every change.
/// </summary>
public interface INicknameDirectory
{
    /// <summary>Gets one player's nickname.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The nickname; empty when the player no longer exists.</returns>
    ValueTask<string> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Gets the nicknames of several players with one database read for those not cached.</summary>
    /// <param name="playerIds">The players' account ids.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>Nickname by id; a player that no longer exists is missing.</returns>
    Task<IReadOnlyDictionary<Guid, string>> GetManyAsync(IEnumerable<Guid> playerIds, CancellationToken cancellationToken = default);

    /// <summary>Records a nickname that was just stored, so later reads see it.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="nickname">The stored nickname.</param>
    void Remember(Guid playerId, string nickname);
}
