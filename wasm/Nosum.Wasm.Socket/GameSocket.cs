// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Nosum.Wasm.Socket;

/// <summary>
/// Owns the one Nalix <see cref="WebSocketSession"/>: connect, handshake, push subscriptions and a
/// reconnect loop with backoff (docs/adr/ADR-003 D5). A lost connection is replaced by a fresh
/// session; subscriptions are re-attached to it.
/// </summary>
public sealed class GameSocket : IGameSocket, IAsyncDisposable
{
    private readonly GameSocketOptions _options;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<ISubscription> _subscriptions = [];
    [SuppressMessage("Usage", "CA2213", Justification = "Borrowed: the run loop owns the session and disposes it when the connection ends.")]
    private WebSocketSession? _session;
    private Task _loop = Task.CompletedTask;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;
    private int _state;

    /// <summary>Initializes the socket; nothing connects until <see cref="Start"/>.</summary>
    /// <param name="options">Where the server is.</param>
    public GameSocket(GameSocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public event Action<GameSocketState>? StateChanged;

    /// <inheritdoc/>
    public event Action? Ready;

    /// <inheritdoc/>
    public GameSocketState State => (GameSocketState)Volatile.Read(ref _state);

    /// <inheritdoc/>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _loop = Task.Run(() => this.RunAsync(_stop.Token));
        }
    }

    /// <inheritdoc/>
    public Task WaitReadyAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return _session is not null ? Task.CompletedTask : _ready.Task.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public ValueTask<TResponse> RequestAsync<TResponse>(IPacket request, CancellationToken cancellationToken = default)
        where TResponse : class, IPacket, IPacketStaticOpcode
    {
        ArgumentNullException.ThrowIfNull(request);
        // Requests are encrypted: account packets carry passwords and tokens, and the server refuses them in plaintext.
        return this.ReadySession().RequestAsync<TResponse>(request, RequestOptions.Default.WithEncrypt(), ct: cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        await this.ReadySession().SendAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IDisposable Subscribe<TPacket>(Action<TPacket> handler)
        where TPacket : class, IPacket, IPacketStaticOpcode
    {
        ArgumentNullException.ThrowIfNull(handler);

        Subscription<TPacket> subscription = new(this, handler);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
            if (_session is { } session)
            {
                subscription.Attach(session);
            }
        }

        return subscription;
    }

    /// <summary>Stops reconnecting and closes the connection.</summary>
    /// <returns>A task that completes when the loop has ended.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "Shutdown: the loop reports its own failures by retrying.")]
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cancelled while connecting.
        }

        _stop.Dispose();
        this.SetState(GameSocketState.Offline);
    }

    private WebSocketSession ReadySession()
    {
        lock (_gate)
        {
            return _session ?? throw new InvalidOperationException("The game socket is not ready.");
        }
    }

    private void SetState(GameSocketState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state)
        {
            this.StateChanged?.Invoke(state);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Any connect failure (DNS, refused, handshake, dropped) means the same thing: wait and try again.")]
    private async Task RunAsync(CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            this.SetState(failures == 0 ? GameSocketState.Connecting : GameSocketState.Reconnecting);
            using WebSocketSession session = this.CreateSession();
            TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            session.OnDisconnected += (_, _) => closed.TrySetResult();
            try
            {
                await session.ConnectAsync(ct: ct).ConfigureAwait(false);
                await session.HandshakeAsync(ct).ConfigureAwait(false);

                lock (_gate)
                {
                    _session = session;
                    _ready.TrySetResult();
                    foreach (ISubscription subscription in _subscriptions)
                    {
                        subscription.Attach(session);
                    }
                }

                failures = 0;
                this.SetState(GameSocketState.Ready);
                this.Ready?.Invoke();
                await closed.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                failures++;
            }
            finally
            {
                lock (_gate)
                {
                    _session = null;
                    if (_ready.Task.IsCompleted)
                    {
                        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    foreach (ISubscription subscription in _subscriptions)
                    {
                        subscription.Detach();
                    }
                }
            }

            this.SetState(GameSocketState.Reconnecting);
            try
            {
                await Task.Delay(this.Backoff(failures), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TimeSpan Backoff(int failures)
    {
        double factor = Math.Pow(2, Math.Min(Math.Max(failures - 1, 0), 10));
        return TimeSpan.FromMilliseconds(Math.Min(_options.ReconnectBaseDelay.TotalMilliseconds * factor, _options.ReconnectMaxDelay.TotalMilliseconds));
    }

    private WebSocketSession CreateSession()
        => new(
            new TransportOptions
            {
                Address = _options.Host,
                Port = _options.Port,
                ServerPublicKey = _options.ServerPublicKey,
            },
            new WebSocketTransportOptions
            {
                Path = _options.Path,
                UseTls = _options.UseTls,
            });

    private void Remove(ISubscription subscription)
    {
        lock (_gate)
        {
            _ = _subscriptions.Remove(subscription);
            subscription.Detach();
        }
    }

    private interface ISubscription
    {
        void Attach(WebSocketSession session);

        void Detach();
    }

    private sealed class Subscription<TPacket> : ISubscription, IDisposable
        where TPacket : class, IPacket, IPacketStaticOpcode
    {
        private readonly GameSocket _owner;
        private readonly Action<TPacket> _handler;
        [SuppressMessage("Usage", "CA2213", Justification = "Disposed by Detach, which Dispose reaches through GameSocket.Remove.")]
        private IDisposable? _inner;

        public Subscription(GameSocket owner, Action<TPacket> handler)
        {
            _owner = owner;
            _handler = handler;
        }

        public void Attach(WebSocketSession session)
        {
            _inner?.Dispose();
            _inner = session.On(_handler);
        }

        public void Detach()
        {
            _inner?.Dispose();
            _inner = null;
        }

        public void Dispose() => _owner.Remove(this);
    }
}
