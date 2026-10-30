// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Emote;
using Nosum.Gameplay.Emotes;
using Nosum.Wasm.Socket.Emote;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.ViewModels.Emotes;

/// <summary>One emote on the wheel: its code, its localized name and the sprite of its icon.</summary>
/// <param name="Code">The emote code, the index in the icon sheet.</param>
/// <param name="Label">The localized name, for the accessible name and the tooltip.</param>
/// <param name="Icon">The theme sprite name of the icon, for example <c>emote-2</c>.</param>
public sealed record EmoteOption(byte Code, string Label, string Icon);

/// <summary>
/// The emote wheel (docs/adr/ADR-075 D1-D2): opened from the hotkey or the HUD button, it sends the chosen emote and, on the server's OK, shows the
/// player's own bubble. A refusal (cooldown, no map) shows its message briefly. Other farmers' emotes go to the same bubble store.
/// </summary>
public sealed class EmoteWheelViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long a refusal message stays on the wheel, milliseconds.</summary>
    public const int NoticeMs = 2_500;

    /// <summary>The browser key code that opens the wheel. It is reserved in <see cref="KeyBindings"/>, so no action can take it.</summary>
    public const string Hotkey = "KeyB";

    private readonly EmoteChannel _channel;
    private readonly EmoteBubbleStore _bubbles;
    private readonly Func<uint> _localPlayerId;
    private readonly IStringLocalizer<Language> _text;
    private readonly Func<long> _clock;
    private readonly Timer _noticeTimer;
    private readonly EmoteOption[] _options;
    private byte _pending;
    private bool _open;
    private string _notice = string.Empty;

    /// <summary>Initializes the view model and starts following the emotes of other farmers.</summary>
    /// <param name="channel">The emote connection.</param>
    /// <param name="bubbles">The bubbles on screen.</param>
    /// <param name="localPlayerId">Returns the local player's id on the map (zero before joining).</param>
    /// <param name="text">UI text.</param>
    /// <param name="clock">Monotonic milliseconds, the origin the bubble store uses; defaults to the tick count.</param>
    public EmoteWheelViewModel(EmoteChannel channel, EmoteBubbleStore bubbles, Func<uint> localPlayerId, IStringLocalizer<Language> text, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(bubbles);
        ArgumentNullException.ThrowIfNull(localPlayerId);
        ArgumentNullException.ThrowIfNull(text);

        _channel = channel;
        _bubbles = bubbles;
        _localPlayerId = localPlayerId;
        _text = text;
        _clock = clock ?? (() => Environment.TickCount64);
        _options = new EmoteOption[EmoteRules.Count];
        for (int code = 0; code < EmoteRules.Count; code++)
        {
            _options[code] = new EmoteOption((byte)code, this.Text("Emote_" + code), "emote-" + code);
        }

        _noticeTimer = new Timer(_ => this.ClearNotice(), null, Timeout.Infinite, Timeout.Infinite);
        channel.RemoteShown += this.OnRemoteShown;
        channel.Completed += this.OnCompleted;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets the emotes of the wheel, in code order.</summary>
    public IReadOnlyList<EmoteOption> Options => _options;

    /// <summary>Gets whether the wheel is open.</summary>
    public bool IsOpen => _open;

    /// <summary>Gets the refusal message shown on the wheel, or an empty string.</summary>
    public string Notice => _notice;

    /// <summary>Gets the accessible name of the wheel.</summary>
    public string Label => this.Text("Emote_Wheel");

    /// <summary>Gets the tooltip of the HUD button, with the hotkey.</summary>
    public string OpenTip => this.Text("Emote_Open") + " [" + KeyBindings.CapOf(Hotkey) + "]";

    /// <summary>Opens the wheel, or closes it when it is open.</summary>
    public void Toggle()
    {
        _open = !_open;
        this.Raise();
    }

    /// <summary>Closes the wheel.</summary>
    public void Close()
    {
        if (_open)
        {
            _open = false;
            this.Raise();
        }
    }

    /// <summary>Sends an emote. The wheel closes; the bubble appears when the server accepts it, a refusal shows its message.</summary>
    /// <param name="code">The emote code.</param>
    public void Choose(byte code)
    {
        if (!EmoteRules.IsValid(code))
        {
            return;
        }

        _pending = code;
        _open = false;
        _channel.Send(code);
        this.Raise();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.RemoteShown -= this.OnRemoteShown;
        _channel.Completed -= this.OnCompleted;
        _noticeTimer.Dispose();
    }

    private void OnRemoteShown(uint playerId, byte emote) => _bubbles.Show(playerId, emote, _clock());

    private void OnCompleted(EmoteStatus status)
    {
        if (status == EmoteStatus.Ok)
        {
            _bubbles.Show(_localPlayerId(), _pending, _clock());
            return;
        }

        string message = status switch
        {
            EmoteStatus.Cooldown => this.Text("Emote_Cooldown"),
            EmoteStatus.NotOnMap => this.Text("Emote_NotOnMap"),
            _ => this.Text("Emote_Invalid"),
        };
        _notice = message;
        _ = _noticeTimer.Change(NoticeMs, Timeout.Infinite);
        this.Raise();
    }

    private void ClearNotice()
    {
        if (_notice.Length > 0)
        {
            _notice = string.Empty;
            this.Raise();
        }
    }

    private string Text(string key) => _text[key].Value;

    private void Raise() => this.Changed?.Invoke();
}
