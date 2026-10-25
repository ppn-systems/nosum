// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;

namespace Nosum.Abstractions.Accounts;

/// <summary>
/// Keeps one active connection per player. When a player signs in again (login, resume, guest resume)
/// while another connection of theirs is open, that older connection is told why, takes its place on
/// the map (and its position is saved) and is closed - before the new connection goes on.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>Makes a connection the player's active one, ending any other connection of the same player.</summary>
    /// <param name="playerId">The signed-in player.</param>
    /// <param name="connection">The connection that just signed in.</param>
    /// <param name="cancellationToken">Cancels the wait for the older connection to be cleaned up.</param>
    /// <returns>A task that completes once the older connection (if any) has been ended and its state saved.</returns>
    ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Forgets a connection (after logout). Does nothing if it is not the player's active one.</summary>
    /// <param name="connection">The connection that signed out.</param>
    void Release(IConnection connection);

    /// <summary>Gets the player's active connection, or null.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The active connection, or null when the player is not connected.</returns>
    IConnection? GetActive(Guid playerId);

    /// <summary>
    /// Ends a player's active connection for a reason an admin gave (docs/adr/ADR-031 D7): the player leaves their map (position saved), is told why
    /// in <c>SessionEnded</c> and is disconnected. The default does nothing, so a test double need not implement it.
    /// </summary>
    /// <param name="playerId">The player.</param>
    /// <param name="reason">The reason sent to the client.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns><see langword="true"/> when the player was connected.</returns>
    ValueTask<bool> EndAsync(Guid playerId, Nosum.Contracts.Account.AccountStatus reason, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);
}
