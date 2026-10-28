// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Player;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Player;

/// <summary>
/// The connection badge: turns the socket state and the join outcome (codes) into text from the
/// localization resources. Raises <see cref="Changed"/> on any thread; a component marshals with
/// <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public sealed class ConnectionStatusViewModel : IObservableViewModel, IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IStringLocalizer<Language> _text;
    private PlayerChannel? _channel;
    private JoinStatus? _rejection;

    /// <summary>Initializes the view model.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="text">Localized UI text.</param>
    public ConnectionStatusViewModel(IGameSocket socket, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(text);

        _socket = socket;
        _text = text;
        socket.StateChanged += this.OnStateChanged;
    }

    /// <summary>Raised when <see cref="Text"/> may have changed.</summary>
    public event Action? Changed;

    /// <summary>Gets the connection state.</summary>
    public GameSocketState State => _socket.State;

    /// <summary>Gets whether the connection badge should be visible (not ready, or a join rejection is active).</summary>
    public bool IsVisible => _rejection is not null || this.State != GameSocketState.Ready;

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

    /// <summary>Starts watching a player channel for join refusals.</summary>
    /// <param name="channel">The channel.</param>
    public void Watch(PlayerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        channel.JoinRejected += this.OnJoinRejected;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _socket.StateChanged -= this.OnStateChanged;
        if (_channel is not null)
        {
            _channel.JoinRejected -= this.OnJoinRejected;
        }
    }

    private void OnStateChanged(GameSocketState state)
    {
        _rejection = null;
        this.Changed?.Invoke();
    }

    private void OnJoinRejected(JoinStatus status)
    {
        _rejection = status;
        this.Changed?.Invoke();
    }
}
