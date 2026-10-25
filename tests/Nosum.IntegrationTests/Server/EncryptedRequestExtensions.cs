// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Requests of the server tests are encrypted like the real client's: account handlers refuse plaintext
/// frames. Being in the tests' own namespace, this overload wins over the SDK's for calls without options.
/// </summary>
internal static class EncryptedRequestExtensions
{
    public static ValueTask<TResponse> RequestAsync<TResponse>(this WebSocketSession session, IPacket request, CancellationToken ct = default)
        where TResponse : class, IPacket, IPacketStaticOpcode
        => RequestExtensions.RequestAsync<TResponse>(session, request, RequestOptions.Default.WithEncrypt(), ct: ct);
}
