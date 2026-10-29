// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Player;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Player;

/// <summary>
/// The connection badge and the reconnect overlay: turns the socket state, the join outcome (codes) and the reconnect countdown into text from the
/// localization resources. Raises <see cref="Changed"/> on any thread; a component marshals with <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public sealed class ConnectionStatusViewModel : IObservableViewModel, IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IStringLocalizer<Language> _text;
    private readonly TimeProvider _time;
    private PlayerChannel? _channel;
    private JoinStatus? _rejection;
    private DateTimeOffset? _retryAt;

    /// <summary>Initializes the view model.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="text">Localized UI text.</param>
    /// <param name="time">The clock the countdown reads; the system clock when <see langword="null"/>.</param>
    public ConnectionStatusViewModel(IGameSocket socket, IStringLocalizer<Language> text, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(text);

        _socket = socket;
        _text = text;
        _time = time ?? TimeProvider.System;
        socket.StateChanged += this.OnStateChanged;
        socket.ReconnectScheduled += this.OnReconnectScheduled;
    }

    /// <summary>Raised when <see cref="Text"/> or the overlay may have changed.</summary>
    public event Action? Changed;

    /// <summary>Gets the connection state.</summary>
    public GameSocketState State => _socket.State;

    /// <summary>Gets whether the connection badge should be visible (not ready, or a join rejection is active).</summary>
    public bool IsVisible => _rejection is not null || this.State != GameSocketState.Ready;

    /// <summary>Gets whether the reconnect overlay covers the game: the connection was lost or is down, and the game waits for it.</summary>
    public bool IsOverlayVisible => this.State is GameSocketState.Reconnecting or GameSocketState.Offline;

    /// <summary>Gets whether game input is held back: every state but a ready connection.</summary>
    public bool BlocksInput => this.State != GameSocketState.Ready;

    /// <summary>Gets the whole seconds until the next reconnect attempt, rounded up; 0 when none is waiting or the attempt is due.</summary>
    public int SecondsUntilRetry
    {
        get
        {
            if (_retryAt is not { } retryAt)
            {
                return 0;
            }

            double remaining = (retryAt - _time.GetUtcNow()).TotalSeconds;
            return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
        }
    }

    /// <summary>Gets the visual severity kind of the badge (<c>"ok"</c>, <c>"warn"</c>, or <c>"error"</c>).</summary>
    public string BadgeKind => _rejection is not null
        ? "error"
        : this.State switch
        {
            GameSocketState.Ready => "ok",
            GameSocketState.Connecting or GameSocketState.Reconnecting => "warn",
            _ => "error",
        };

    /// <summary>Gets the visual severity kind of the badge (alias for <see cref="BadgeKind"/>).</summary>
    public string Kind => this.BadgeKind;

    /// <summary>Gets the text to show: a join refusal if there is one, otherwise the connection state.</summary>
    public string Text => _rejection switch
    {
        JoinStatus.UnknownMap => _text["Join_UnknownMap"].Value,
        _ => _socket.State switch
        {
            GameSocketState.Connecting => _text["Connection_Connecting"].Value,
            GameSocketState.Ready => _text["Connection_Online"].Value,
            GameSocketState.Reconnecting => _text["Connection_Reconnecting"].Value,
            GameSocketState.KeyMismatch => _text["Connection_KeyMismatch"].Value,
            _ => _text["Connection_Offline"].Value,
        },
    };

    /// <summary>Gets the overlay's heading.</summary>
    public string OverlayTitle => this.State == GameSocketState.Offline
        ? _text["Connection_Offline"].Value
        : _text["Connection_LostTitle"].Value;

    /// <summary>Gets the overlay's message: the countdown, the attempt in progress, or that the server cannot be reached.</summary>
    public string OverlayMessage => this.State switch
    {
        GameSocketState.Offline => _text["Connection_OfflineMessage"].Value,
        _ => this.SecondsUntilRetry > 0
            ? _text["Connection_RetryIn", this.SecondsUntilRetry].Value
            : _text["Connection_Retrying"].Value,
    };

    /// <summary>Gets the overlay's second line: the game waits for the connection.</summary>
    public string OverlayDetail => _text["Connection_Paused"].Value;

    /// <summary>Gets the label of the button that skips the wait.</summary>
    public string RetryNowText => _text["Connection_RetryNow"].Value;

    /// <summary>Starts watching a player channel for join refusals.</summary>
    /// <param name="channel">The channel.</param>
    public void Watch(PlayerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        channel.JoinRejected += this.OnJoinRejected;
    }

    /// <summary>Skips the wait before the next reconnect attempt and tries now. Does nothing while an attempt is already running.</summary>
    public void RetryNow() => _socket.RetryNow();

    /// <summary>
    /// Re-raises <see cref="Changed"/> while the overlay is up, so the countdown reads right. The overlay's timer calls it once a second; it does nothing
    /// otherwise.
    /// </summary>
    public void Tick()
    {
        if (this.IsOverlayVisible)
        {
            this.Changed?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _socket.StateChanged -= this.OnStateChanged;
        _socket.ReconnectScheduled -= this.OnReconnectScheduled;
        if (_channel is not null)
        {
            _channel.JoinRejected -= this.OnJoinRejected;
        }
    }

    private void OnStateChanged(GameSocketState state)
    {
        _rejection = null;
        if (state == GameSocketState.Ready)
        {
            _retryAt = null;
        }

        this.Changed?.Invoke();
    }

    private void OnReconnectScheduled(TimeSpan delay)
    {
        _retryAt = _time.GetUtcNow() + delay;
        this.Changed?.Invoke();
    }

    private void OnJoinRejected(JoinStatus status)
    {
        _rejection = status;
        this.Changed?.Invoke();
    }
}
