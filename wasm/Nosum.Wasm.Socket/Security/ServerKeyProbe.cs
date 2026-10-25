// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Protocols;
using Nalix.Codec.ProtocolFrames;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Nosum.Wasm.Socket.Security;

/// <summary>
/// Asks a server for its public key without trusting it for anything else: the key is only compared with what the client already holds, or
/// remembered on first use. Everything that follows is checked by the handshake against that key.
/// </summary>
public static class ServerKeyProbe
{
    /// <summary>How long to wait for the server's answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Connects, asks for the public key and disconnects.</summary>
    /// <param name="transport">Where the server is.</param>
    /// <param name="webSocket">The WebSocket settings.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The key as normalized hex.</returns>
    /// <exception cref="InvalidOperationException">The server answered with something that is not a key.</exception>
    public static async Task<string> FetchAsync(TransportOptions transport, WebSocketTransportOptions webSocket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(webSocket);

        using WebSocketSession session = new(transport, webSocket);
        await session.ConnectAsync(ct: cancellationToken).ConfigureAwait(false);
        await session.SendControlAsync(ControlType.PUBLIC_KEY_REQUEST, static _ => { }, cancellationToken).ConfigureAwait(false);
        SessionTofu reply = await session.AwaitPacketAsync<SessionTofu>(static _ => true, (int)Timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
        string key = reply.PublicKey.ToString();
        return ServerKeys.TryNormalize(key, out string hex) ? hex : throw new InvalidOperationException("The server did not answer with a public key.");
    }
}
