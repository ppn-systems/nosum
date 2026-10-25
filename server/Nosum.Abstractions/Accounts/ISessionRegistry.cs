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
    /// <summary>
    /// Raised after a player's last connection ended (a logout, a closed socket, an ended session), not when a new sign-in replaces the old connection. Caches that hold
    /// a signed-in player's state let go of it here (docs/adr/ADR-035); handlers run off the closing path and their failures are swallowed.
    /// </summary>
    event Func<Guid, ValueTask>? SessionEnded
    {
        add { }
        remove { }
    }

    /// <summary>Makes a connection the player's active one, ending any other connection of the same player.</summary>
    /// <param name="playerId">The signed-in player.</param>
    /// <param name="connection">The connection that just signed in.</param>
    /// <param name="cancellationToken">Cancels the wait for the older connection to be cleaned up.</param>
    /// <returns>A task that completes once the older connection (if any) has been ended and its state saved.</returns>
    ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Forgets a connection (after logout). Does nothing if it is not the player's active one.</summary>
    /// <param name="connection">The connection that signed out.</param>
    void Release(IConnection connection);

    /// <summary>
    /// Ends the player's active connection, if any: the player leaves the map (position saved), is told why (<c>SessionEnded</c>) and the connection
    /// is closed. Used when a password is reset (docs/adr/ADR-032) and when an admin kicks or bans (docs/adr/ADR-031 D7).
    /// </summary>
    /// <param name="playerId">The player whose sessions all ended.</param>
    /// <param name="reason">Sent to the connection as the reason.</param>
    /// <param name="cancellationToken">Cancels the wait for the connection to be cleaned up.</param>
    /// <returns><see langword="true"/> when the player was connected, once the connection has been ended and its state saved.</returns>
    ValueTask<bool> EndAsync(Guid playerId, Nosum.Contracts.Account.AccountStatus reason, CancellationToken cancellationToken = default);

    /// <summary>Gets the player's active connection, or null.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The active connection, or null when the player is not connected.</returns>
    IConnection? GetActive(Guid playerId);
}
