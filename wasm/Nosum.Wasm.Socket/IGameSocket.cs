// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;

namespace Nosum.Wasm.Socket;

/// <summary>
/// The game's single server connection, for any feature: typed requests, fire-and-forget sends and
/// push subscriptions that survive reconnects. Pages and view models reach it only through
/// view models (docs/adr/ADR-003 D5).
/// </summary>
public interface IGameSocket
{
    /// <summary>Raised when <see cref="State"/> changes. May run on any thread.</summary>
    event Action<GameSocketState>? StateChanged;

    /// <summary>
    /// Raised each time the connection becomes ready, the first time and after every reconnect. A
    /// feature that needs server-side state (joining a map) rebuilds it here. May run on any thread.
    /// </summary>
    event Action? Ready;

    /// <summary>
    /// Raised when the socket has scheduled its next reconnect attempt, with the wait before it. Raised before the wait starts, so a view can count it
    /// down. May run on any thread.
    /// </summary>
    event Action<TimeSpan>? ReconnectScheduled;

    /// <summary>Gets the current state.</summary>
    GameSocketState State { get; }

    /// <summary>Starts connecting in the background and keeps the connection alive. Calling it again does nothing.</summary>
    void Start();

    /// <summary>Ends the wait before the next reconnect attempt, so the attempt starts now. Does nothing while an attempt is running or the socket is ready.</summary>
    void RetryNow();

    /// <summary>Completes when the socket is ready, immediately if it already is.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when <see cref="State"/> is <see cref="GameSocketState.Ready"/>.</returns>
    Task WaitReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a request and waits for the reply that carries its sequence id.</summary>
    /// <typeparam name="TResponse">Reply packet type.</typeparam>
    /// <param name="request">The request; the caller keeps ownership.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="InvalidOperationException">The socket is not ready.</exception>
    ValueTask<TResponse> RequestAsync<TResponse>(IPacket request, CancellationToken cancellationToken = default)
        where TResponse : class, IPacket, IPacketStaticOpcode;

    /// <summary>Sends a packet without waiting for a reply.</summary>
    /// <param name="packet">The packet; the caller keeps ownership.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the packet was written.</returns>
    /// <exception cref="InvalidOperationException">The socket is not ready.</exception>
    ValueTask SendAsync(IPacket packet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to a pushed packet type. The handler keeps working across reconnects until the
    /// returned handle is disposed. The packet is pooled: read it inside the handler and keep no reference.
    /// </summary>
    /// <typeparam name="TPacket">Pushed packet type.</typeparam>
    /// <param name="handler">Called for every such packet. May run on any thread.</param>
    /// <returns>A handle that removes the subscription.</returns>
    IDisposable Subscribe<TPacket>(Action<TPacket> handler)
        where TPacket : class, IPacket, IPacketStaticOpcode;
}
