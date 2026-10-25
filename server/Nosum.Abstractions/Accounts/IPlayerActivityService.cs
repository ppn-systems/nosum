// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Accounts;

/// <summary>
/// Records that a player is still around. Sign-in and resume do it already; features that keep a
/// player online for long (a farm session, a co-op) call it periodically. Idle guests are deleted.
/// </summary>
public interface IPlayerActivityService
{
    /// <summary>Marks the player active now.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the activity is stored.</returns>
    Task TouchAsync(Guid playerId, CancellationToken cancellationToken);
}
