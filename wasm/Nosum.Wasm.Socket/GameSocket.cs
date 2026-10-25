// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Nalix.Abstractions.Networking.Packets;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Wasm.Socket.Security;

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
    private readonly string[] _pins;
    private int _started;
    private int _state;
    private volatile bool _keyMismatch;
    private string? _goodKey;
    private string? _firstUseKey;

    /// <summary>Initializes the socket; nothing connects until <see cref="Start"/>.</summary>
    /// <param name="options">Where the server is.</param>
    public GameSocket(GameSocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _pins = PinnedKeys(options);
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
            this.SetState(_keyMismatch ? GameSocketState.KeyMismatch : failures == 0 ? GameSocketState.Connecting : GameSocketState.Reconnecting);
            TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            WebSocketSession? session = null;
            try
            {
                session = await this.OpenAsync(closed, ct).ConfigureAwait(false);

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
                _keyMismatch = false;
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

                session?.Dispose();
            }

            this.SetState(_keyMismatch ? GameSocketState.KeyMismatch : GameSocketState.Reconnecting);
            try
            {
                await Task.Delay(_keyMismatch ? _options.ReconnectMaxDelay : this.Backoff(failures), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Connects and handshakes with the first key that works. A pinned deployment tries each pinned key (the current one and, during a rotation,
    // the next); a development build trusts what it remembered, or learns the key on first use. A server that proves none of them is refused.
    [SuppressMessage("Design", "CA1031", Justification = "A failed attempt with one key is recorded and the next key is tried; the last failure is rethrown.")]
    private async Task<WebSocketSession> OpenAsync(TaskCompletionSource closed, CancellationToken ct)
    {
        string?[] candidates = await this.CandidatesAsync(ct).ConfigureAwait(false);
        Exception? last = null;
        foreach (string? key in candidates)
        {
            WebSocketSession session = this.CreateSession(key);
            session.OnDisconnected += (_, _) => closed.TrySetResult();
            try
            {
                await session.ConnectAsync(ct: ct).ConfigureAwait(false);
                await session.HandshakeAsync(ct).ConfigureAwait(false);
                _goodKey = key;
                if (_firstUseKey is { } learned && _options.KeyStore is { } store)
                {
                    // Remembered only once the handshake proved the server holds the key.
                    await store.SetAsync(learned).ConfigureAwait(false);
                    _firstUseKey = null;
                }

                return session;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                session.Dispose();
                last = ex;
            }
        }

        if (candidates.Any(static k => k is not null))
        {
            await this.DetectKeyMismatchAsync(candidates, ct).ConfigureAwait(false);
        }

        throw last ?? new InvalidOperationException("No server key to try.");
    }

    private async Task<string?[]> CandidatesAsync(CancellationToken ct)
    {
        if (_pins.Length > 0)
        {
            // The key that worked last time goes first, so a rotation costs one failed handshake once.
            return [.. _pins.OrderByDescending(k => string.Equals(k, _goodKey, StringComparison.Ordinal))];
        }

        if (_options.KeyStore is not { } store)
        {
            return [null];
        }

        string? stored = await store.GetAsync().ConfigureAwait(false);
        if (ServerKeys.TryNormalize(stored, out string remembered))
        {
            return [remembered];
        }

        // First use: learn the key now, remember it after the handshake.
        string learned = await ServerKeyProbe.FetchAsync(this.TransportFor(null), this.WebSocketOptions(), ct).ConfigureAwait(false);
        _firstUseKey = learned;
        return [learned];
    }

    // A handshake that failed against a pinned or remembered key is either a network problem or an impostor: ask the server which key it has.
    [SuppressMessage("Design", "CA1031", Justification = "The probe is a diagnosis; if it fails the failure stays an ordinary connection failure.")]
    private async Task DetectKeyMismatchAsync(string?[] candidates, CancellationToken ct)
    {
        try
        {
            string presented = await ServerKeyProbe.FetchAsync(this.TransportFor(null), this.WebSocketOptions(), ct).ConfigureAwait(false);
            if (!candidates.Any(k => ServerKeys.AreSame(k, presented)))
            {
                _keyMismatch = true;
            }
        }
        catch (Exception)
        {
            // Cannot reach the server or no answer: nothing is known about its key.
        }
    }

    private static string[] PinnedKeys(GameSocketOptions options)
    {
        List<string> pins = [];
        foreach (string? entry in options.ServerPublicKeys.Prepend(options.ServerPublicKey))
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            string key = ServerKeys.TryNormalize(entry, out string hex) ? hex : throw new FormatException("A pinned server public key is not a 64-digit hex key.");
            if (!pins.Contains(key))
            {
                pins.Add(key);
            }
        }

        if (options.RequirePin && pins.Count == 0)
        {
            throw new FormatException("A server public key must be pinned (Server:RequirePin is set).");
        }

        return pins.Count <= ServerKeys.MaxPinned ? [.. pins] : throw new FormatException("At most " + ServerKeys.MaxPinned + " server public keys may be pinned.");
    }

    private TimeSpan Backoff(int failures)
    {
        double factor = Math.Pow(2, Math.Min(Math.Max(failures - 1, 0), 10));
        return TimeSpan.FromMilliseconds(Math.Min(_options.ReconnectBaseDelay.TotalMilliseconds * factor, _options.ReconnectMaxDelay.TotalMilliseconds));
    }

    private WebSocketSession CreateSession(string? pinnedKey) => new(this.TransportFor(pinnedKey), this.WebSocketOptions());

    // With a key the SDK verifies the server's proof against it and never falls back to trusting what it is shown.
    private TransportOptions TransportFor(string? pinnedKey)
        => new()
        {
            Address = _options.Host,
            Port = _options.Port,
            ServerPublicKey = pinnedKey,
            AllowTofu = pinnedKey is null,
        };

    private WebSocketTransportOptions WebSocketOptions()
        => new()
        {
            Path = _options.Path,
            UseTls = _options.UseTls,
        };

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
