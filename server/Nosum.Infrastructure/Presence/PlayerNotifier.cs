// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// <see cref="IPlayerNotifier"/> over the session registry: finds the player's active connection and
/// sends the packet there.
/// </summary>
public sealed class PlayerNotifier : IPlayerNotifier
{
    private readonly ISessionRegistry _sessions;

    /// <summary>Initializes the notifier.</summary>
    /// <param name="sessions">Who is connected.</param>
    public PlayerNotifier(ISessionRegistry sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
    }

    /// <summary>
    /// Gets or sets whether pushes are encrypted. Encryption needs the secure handshake
    /// (<c>UseSecureConnections()</c>); the composition root turns it off only for plaintext development.
    /// </summary>
    public bool EncryptSends { get; set; } = true;

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Best effort: a closing connection is cleaned up by its close event and the player reads the state when they next ask.")]
    public async ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        IConnection? connection = _sessions.GetActive(playerId);
        if (connection is null || connection.IsDisposed)
        {
            return false;
        }

        try
        {
            await connection.SendAsync(packet, NetworkTransport.TCP, enableEncrypt: this.EncryptSends, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
