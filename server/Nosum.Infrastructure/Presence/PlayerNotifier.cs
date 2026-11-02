// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Presence;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// <see cref="IPlayerNotifier"/> over the session registry: finds the player's active connection and
/// sends the packet there, under the send deadline (docs/adr/ADR-087 D6).
/// </summary>
public sealed class PlayerNotifier : IPlayerNotifier
{
    /// <summary>The send deadline of a single push: the same 250 ms as a viewer's snapshot and a fan-out (docs/adr/ADR-087 D2, D6).</summary>
    public static readonly TimeSpan DefaultSendDeadline = TimeSpan.FromMilliseconds(250);

    private readonly ISessionRegistry _sessions;
    private readonly Func<IConnection, IPacket, bool, CancellationToken, ValueTask> _send;

    /// <summary>Initializes the notifier.</summary>
    /// <param name="sessions">Who is connected.</param>
    /// <param name="send">
    /// Sends one packet over the connection's TCP transport. Null for the real transport; a test passes its own to stand in for a stalled or
    /// healthy client without a socket.
    /// </param>
    public PlayerNotifier(ISessionRegistry sessions, Func<IConnection, IPacket, bool, CancellationToken, ValueTask>? send = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
        _send = send ?? SendOverTransportAsync;
    }

    /// <summary>
    /// Gets or sets whether pushes are encrypted. Encryption needs the secure handshake
    /// (<c>UseSecureConnections()</c>); the composition root turns it off only for plaintext development.
    /// </summary>
    public bool EncryptSends { get; set; } = true;

    /// <summary>
    /// Gets or sets how long the player has to take the packet. A player who does not take it by then is disconnected, as a recipient of a
    /// fan-out is (docs/adr/ADR-087 D6): a stalled connection must not hold the handler that pushed to it. Defaults to <see cref="DefaultSendDeadline"/>.
    /// </summary>
    public TimeSpan SendDeadline { get; set; } = DefaultSendDeadline;

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

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(this.SendDeadline);
        try
        {
            await _send(connection, packet, this.EncryptSends, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The player did not take the packet within the deadline: drop the connection. Its close event then removes the player from presence.
            OpsLog.Warning("notifier.send_deadline", "A player did not take a pushed packet within the send deadline and was disconnected.", new LogField("deadline_ms", this.SendDeadline.TotalMilliseconds));
            try
            {
                connection.Disconnect("push send deadline exceeded");
            }
            catch (Exception)
            {
                // Already closing.
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async ValueTask SendOverTransportAsync(IConnection connection, IPacket packet, bool enableEncrypt, CancellationToken cancellationToken)
        => await connection.SendAsync(packet, NetworkTransport.TCP, enableEncrypt: enableEncrypt, cancellationToken).ConfigureAwait(false);
}
