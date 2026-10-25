// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.S2C;

namespace Nosum.Infrastructure.Accounts;

/// <summary>In-memory <see cref="ISessionRegistry"/> for a single server process.</summary>
public sealed class SessionRegistry : ISessionRegistry
{
    private readonly IMapPresence _presence;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, IConnection> _active = [];
    private readonly Dictionary<ulong, Guid> _playerOf = [];
    private readonly EventHandler<IConnectionEventArgs> _onClosed;

    /// <summary>Initializes the registry.</summary>
    /// <param name="presence">Map presence: the older connection leaves its map (saving its place) before it is closed.</param>
    public SessionRegistry(IMapPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        _presence = presence;
        _onClosed = (_, e) => this.Release(e.Connection);
    }

    /// <summary>Gets or sets whether the notice to the older connection is encrypted; off only for plaintext development.</summary>
    public bool EncryptSends { get; set; } = true;

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Ending the older connection is best effort: it may already be closing, and the new sign-in must still succeed.")]
    public async ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IConnection? previous;
        lock (_gate)
        {
            _ = _active.TryGetValue(playerId, out previous);
            if (previous is not null && previous.ConnectionId == connection.ConnectionId)
            {
                return;
            }

            // A connection that signs in as another player first leaves its earlier player.
            if (_playerOf.TryGetValue(connection.ConnectionId, out Guid earlier) && earlier != playerId)
            {
                _ = _active.Remove(earlier);
            }
            else if (!_playerOf.ContainsKey(connection.ConnectionId))
            {
                connection.ConnectionClosed += _onClosed;
            }

            _active[playerId] = connection;
            _playerOf[connection.ConnectionId] = playerId;
            if (previous is not null)
            {
                _ = _playerOf.Remove(previous.ConnectionId);
            }
        }

        if (previous is null)
        {
            return;
        }

        await this.CloseAsync(previous, AccountStatus.SessionReplaced, "session replaced", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> EndAsync(Guid playerId, AccountStatus reason, CancellationToken cancellationToken = default)
    {
        IConnection? connection;
        lock (_gate)
        {
            if (!_active.TryGetValue(playerId, out connection))
            {
                return false;
            }

            _ = _active.Remove(playerId);
            _ = _playerOf.Remove(connection.ConnectionId);
        }

        await this.CloseAsync(connection, reason, "ended by an admin", cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Out of the game first, so its position is saved once and a late move from it cannot overwrite a newer one; then told why; then closed.
    [SuppressMessage("Design", "CA1031", Justification = "Ending a connection is best effort: it may already be closing, and the caller must still go on.")]
    private async ValueTask CloseAsync(IConnection connection, AccountStatus reason, string disconnectReason, CancellationToken cancellationToken)
    {
        try
        {
            connection.Level = PermissionLevel.NONE;
            await _presence.LeaveAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!connection.IsDisposed)
            {
                using PacketScope<SessionEnded> lease = PacketFactory<SessionEnded>.Acquire();
                lease.Value.Reason = reason;
                await connection.SendAsync(lease.Value, NetworkTransport.TCP, enableEncrypt: this.EncryptSends, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Fall through to the disconnect.
        }
        finally
        {
            connection.ConnectionClosed -= _onClosed;
            connection.Disconnect(disconnectReason);
        }
    }

    /// <inheritdoc/>
    public void Release(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_gate)
        {
            if (_playerOf.Remove(connection.ConnectionId, out Guid playerId)
                && _active.TryGetValue(playerId, out IConnection? current)
                && current.ConnectionId == connection.ConnectionId)
            {
                _ = _active.Remove(playerId);
            }
        }

        connection.ConnectionClosed -= _onClosed;
    }

    /// <inheritdoc/>
    public IConnection? GetActive(Guid playerId)
    {
        lock (_gate)
        {
            return _active.GetValueOrDefault(playerId);
        }
    }
}
