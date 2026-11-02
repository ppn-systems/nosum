// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Presence;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// The one way a server push reaches many players (docs/adr/ADR-087, amendment 2026-11-02). Every recipient's send starts at once under one
/// deadline, and the call returns when the last send ends or the deadline passes. A recipient that does not take the packet by then is
/// disconnected, as a viewer that misses its snapshot deadline is; the others never wait on it.
/// </summary>
internal sealed class ViewerFanout
{
    /// <summary>The send deadline of a push: the same 250 ms as a viewer's snapshot (docs/adr/ADR-087 D2).</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromMilliseconds(250);

    private readonly IMapPresence _presence;
    private readonly TimeSpan _deadline;

    /// <summary>Initializes the fan-out.</summary>
    /// <param name="presence">Sends one packet to one connection, with the transport and encryption of the presence.</param>
    /// <param name="deadline">How long every recipient has to take the packet; <see cref="DefaultDeadline"/> when null.</param>
    public ViewerFanout(IMapPresence presence, TimeSpan? deadline = null)
    {
        ArgumentNullException.ThrowIfNull(presence);

        _presence = presence;
        _deadline = deadline ?? DefaultDeadline;
    }

    /// <summary>Sends <paramref name="packet"/> to every recipient except <paramref name="except"/>, each under the same deadline.</summary>
    /// <param name="recipients">The connections to send to. The list is read only and may be a pooled buffer.</param>
    /// <param name="packet">The packet to send; the same instance goes to every recipient.</param>
    /// <param name="except">A connection that is left out, or null.</param>
    /// <param name="cancellationToken">The caller's token. Cancelling it ends the wait without disconnecting anyone.</param>
    [SuppressMessage("Design", "CA1031", Justification = "A send that fails for any reason but the deadline is a closing connection; its close event cleans it up.")]
    public async ValueTask SendAsync(IReadOnlyList<IConnection> recipients, IPacket packet, IConnection? except, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(packet);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_deadline);
        CancellationToken token = deadline.Token;

        // Start every send before awaiting any: a stalled recipient holds only its own send.
        List<(IConnection Connection, Task Send)> pending = [];
        for (int i = 0; i < recipients.Count; i++)
        {
            IConnection connection = recipients[i];
            if (ReferenceEquals(connection, except) || connection.IsDisposed)
            {
                continue;
            }

            pending.Add((connection, this.SendOneAsync(connection, packet, token)));
        }

        foreach ((IConnection connection, Task send) in pending)
        {
            try
            {
                await send.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Read below: a send that did not end because the deadline fired is a stuck viewer.
            }
            catch (Exception)
            {
                // Read below: a failed send is not a stuck one; its close event cleans it up.
            }

            if (!send.IsCompletedSuccessfully && deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                this.Drop(connection);
            }
        }
    }

    // A send that fails for any reason other than the deadline completes: a closing connection is cleaned up by its close event.
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort push: one dead connection must not cost the rest of the recipients their packet.")]
    private async Task SendOneAsync(IConnection connection, IPacket packet, CancellationToken token)
    {
        try
        {
            await _presence.SendAsync(connection, packet, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // A closing connection is cleaned up by its close event.
        }
    }

    // The viewer missed its deadline: drop the connection. Its close event then removes the player from presence.
    [SuppressMessage("Design", "CA1031", Justification = "The connection may already be closing; its close event cleans it up.")]
    private void Drop(IConnection connection)
    {
        OpsLog.Warning("fanout.viewer_dropped", "A player did not take a pushed packet within the send deadline and was disconnected.", new LogField("deadline_ms", _deadline.TotalMilliseconds));
        try
        {
            connection.Disconnect("push send deadline exceeded");
        }
        catch (Exception)
        {
            // Already closing.
        }
    }
}
