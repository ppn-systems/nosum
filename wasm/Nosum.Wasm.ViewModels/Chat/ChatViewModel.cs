// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Chat;
using Nosum.Wasm.Socket.Chat;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Chat;

/// <summary>How a chat line is drawn.</summary>
public enum ChatLineKind
{
    /// <summary>A line a player said on local, map or co-op chat.</summary>
    Chat = 0,

    /// <summary>A whisper, sent or received.</summary>
    Whisper = 1,

    /// <summary>A note from the game (a confirmation, a hint).</summary>
    System = 2,

    /// <summary>A refusal from the server or a mistake in what was typed.</summary>
    Error = 3,
}

/// <summary>One chat line ready to draw. <see cref="Text"/> is player-authored for <see cref="ChatLineKind.Chat"/> and <see cref="ChatLineKind.Whisper"/> and must be rendered as plain text, never as markup.</summary>
/// <param name="Id">A number that grows with every line, usable as a render key.</param>
/// <param name="Kind">How the line is drawn.</param>
/// <param name="Channel">The channel of a chat line or whisper.</param>
/// <param name="Tag">The localized channel name in front of the line; empty for a system line.</param>
/// <param name="Sender">The localized sender (for a whisper "A → B"); empty for a system line.</param>
/// <param name="Nickname">The sender's nickname as the server stores it, for answering with a whisper; empty for a system line.</param>
/// <param name="Text">The text.</param>
public sealed record ChatLineView(long Id, ChatLineKind Kind, ChatChannel Channel, string Tag, string Sender, string Nickname, string Text);

/// <summary>
/// View model of the chat line at the bottom-left of the HUD (docs/adr/ADR-029 D7): keeps the last lines, the channel the next line goes to and
/// whether the input box is open, and sends what is typed. Lines fade after <see cref="FadeMs"/>; collapsed, only the last <see cref="CollapsedLines"/>
/// lines that have not faded show. Player text is kept and handed out as plain strings. System lines are codes rendered from the resources.
/// </summary>
public sealed class ChatViewModel : PanelViewModel, IDisposable
{
    /// <summary>How many lines are kept.</summary>
    public const int MaxLines = 100;

    /// <summary>How long a line stays on screen while the input box is closed, in milliseconds.</summary>
    public const long FadeMs = 8000;

    /// <summary>How many lines show while the input box is closed.</summary>
    public const int CollapsedLines = 3;

    private readonly ChatMessenger _chat;
    private readonly NicknameFormatter _names;
    private readonly Func<bool> _isGuest;
    private readonly Func<long> _clock;
    private readonly Timer? _tick;
    private readonly object _gate = new();
    private readonly List<Entry> _lines = [];
    private long _nextId = 1;
    private long _stamp = -1;
    private bool _open;
    private ChatChannel _channel = ChatChannel.Local;
    private string _draft = string.Empty;

    /// <summary>Initializes the view model.</summary>
    /// <param name="chat">The chat channel.</param>
    /// <param name="names">Formats nicknames for display.</param>
    /// <param name="text">UI text.</param>
    /// <param name="isGuest">Tells whether the player is a guest, who may only use local chat.</param>
    /// <param name="clock">The time in Unix milliseconds that lines are dated with; the wall clock when <see langword="null"/>.</param>
    /// <param name="ticks">Whether a timer re-renders the line as lines fade; tests switch it off and call <see cref="Tick"/>.</param>
    public ChatViewModel(ChatMessenger chat, NicknameFormatter names, IStringLocalizer<Language> text, Func<bool>? isGuest = null, Func<long>? clock = null, bool ticks = true)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(names);

        _chat = chat;
        _names = names;
        _isGuest = isGuest ?? (() => false);
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        chat.Message += this.OnMessage;
        chat.Completed += this.OnCompleted;
        if (ticks)
        {
            _tick = new Timer(_ => this.Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Gets whether the input box is open.</summary>
    public bool InputOpen
    {
        get
        {
            lock (_gate)
            {
                return _open;
            }
        }
    }

    /// <summary>Gets the channel the next plain line is sent on: local, map or co-op.</summary>
    public ChatChannel Channel
    {
        get
        {
            lock (_gate)
            {
                return _channel;
            }
        }
    }

    /// <summary>Gets or sets the text in the input box (the control keeps it in step with the field).</summary>
    public string Draft
    {
        get
        {
            lock (_gate)
            {
                return _draft;
            }
        }

        set
        {
            lock (_gate)
            {
                _draft = value ?? string.Empty;
            }
        }
    }

    /// <summary>Gets whether the player is a guest, who may only use local chat.</summary>
    public bool IsGuest => _isGuest();

    /// <summary>Gets the localized name of the current channel, shown in front of the input box.</summary>
    public string ChannelLabel => this.Text("Chat_Channel_" + this.Channel);

    /// <summary>Gets the placeholder of the input box: the commands, or for a guest the local-only note.</summary>
    public string Placeholder => this.Text(this.IsGuest ? "Chat_GuestHint" : "Chat_InputHint");

    /// <summary>Gets the accessible name of the input box.</summary>
    public string InputLabel => this.Text("Chat_InputLabel");

    /// <summary>Gets the longest line the input box takes: a message's limit plus room for a <c>/w nickname </c> prefix (the server limits the message itself).</summary>
    public static int MaxInputLength => Nosum.Gameplay.Chat.ChatRules.MaxLength + 40;

    /// <summary>Gets the lines to draw at <paramref name="nowMs"/>: with the input box open every kept line, otherwise the last few that have not faded.</summary>
    /// <param name="nowMs">The time in Unix milliseconds on the same clock the lines were dated with.</param>
    /// <returns>The lines, oldest first.</returns>
    public ChatLineView[] VisibleLines(long nowMs)
    {
        lock (_gate)
        {
            List<ChatLineView> views = [];
            if (_open)
            {
                foreach (Entry entry in _lines)
                {
                    views.Add(this.Render(entry));
                }

                return [.. views];
            }

            int first = _lines.Count;
            while (first > 0 && _lines.Count - first < CollapsedLines && nowMs - _lines[first - 1].AtMs < FadeMs)
            {
                first--;
            }

            for (int i = first; i < _lines.Count; i++)
            {
                views.Add(this.Render(_lines[i]));
            }

            return [.. views];
        }
    }

    /// <summary>Gets the current time on the clock the lines are dated with, for <see cref="VisibleLines"/>.</summary>
    /// <returns>Unix milliseconds.</returns>
    public long Now() => _clock();

    /// <summary>Opens the input box.</summary>
    public void OpenInput()
    {
        lock (_gate)
        {
            if (_open)
            {
                return;
            }

            _open = true;
            _draft = string.Empty;
        }

        this.Raise();
    }

    /// <summary>Closes the input box and drops what was typed.</summary>
    public void CloseInput()
    {
        lock (_gate)
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _draft = string.Empty;
        }

        this.Raise();
    }

    /// <summary>Moves to the next channel the player may use: local, map, co-op, local... A guest stays on local.</summary>
    public void CycleChannel()
    {
        if (this.IsGuest)
        {
            return;
        }

        lock (_gate)
        {
            _channel = _channel switch
            {
                ChatChannel.Local => ChatChannel.Map,
                ChatChannel.Map => ChatChannel.Coop,
                _ => ChatChannel.Local,
            };
        }

        this.Raise();
    }

    /// <summary>Puts the start of a whisper to <paramref name="nickname"/> into the input box (clicking a name in the log).</summary>
    /// <param name="nickname">The nickname as the server stores it.</param>
    public void StartWhisper(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        if (nickname.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _open = true;
            _draft = "/w " + nickname + " ";
        }

        this.Raise();
    }

    /// <summary>Handles a line typed into the input box: sends it, switches channel, or runs a command. Sending closes the box; a mistake keeps it open with the text.</summary>
    /// <param name="typed">What was typed.</param>
    public void Submit(string? typed)
    {
        ChatCommand command = ChatCommand.Parse(typed, this.Channel);
        switch (command.Kind)
        {
            case ChatCommandKind.None:
                this.CloseInput();
                return;
            case ChatCommandKind.Say:
                if (command.Channel != ChatChannel.Local && this.IsGuest)
                {
                    this.KeepOpen(typed, "Chat_Status_GuestLocalOnly", error: true);
                    return;
                }

                _chat.Send(command.Channel, string.Empty, command.Text);
                break;
            case ChatCommandKind.Whisper:
                _chat.Send(ChatChannel.Whisper, command.Target, command.Text);
                break;
            case ChatCommandKind.Switch:
                if (command.Channel != ChatChannel.Local && this.IsGuest)
                {
                    this.KeepOpen(string.Empty, "Chat_Status_GuestLocalOnly", error: true);
                    return;
                }

                lock (_gate)
                {
                    _channel = command.Channel;
                    _draft = string.Empty;
                }

                this.Raise();
                return;
            case ChatCommandKind.Block:
                _chat.Block(command.Target);
                break;
            case ChatCommandKind.Unblock:
                _chat.Unblock(command.Target);
                break;
            case ChatCommandKind.Report:
                _chat.Report(command.Target);
                break;
            case ChatCommandKind.Usage:
                this.KeepOpen(typed, "Chat_Sys_Usage", error: true);
                return;
            default:
                this.KeepOpen(typed, "Chat_Sys_UnknownCommand", error: true);
                return;
        }

        this.CloseInput();
    }

    /// <summary>Re-renders the line if lines faded since the last time (a timer calls this once a second; the control reads <see cref="VisibleLines"/>).</summary>
    public void Tick()
    {
        long stamp;
        lock (_gate)
        {
            stamp = this.StampLocked(_clock());
            if (stamp == _stamp)
            {
                return;
            }

            _stamp = stamp;
        }

        this.Raise();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _tick?.Dispose();
        _chat.Message -= this.OnMessage;
        _chat.Completed -= this.OnCompleted;
    }

    // What is on screen now, as one number: the id of the oldest line shown and how many are shown. Equal numbers mean the same picture.
    private long StampLocked(long nowMs)
    {
        if (_open)
        {
            return -2 - _lines.Count;
        }

        int shown = 0;
        long firstId = 0;
        for (int i = _lines.Count - 1; i >= 0 && shown < CollapsedLines && nowMs - _lines[i].AtMs < FadeMs; i--)
        {
            shown++;
            firstId = _lines[i].Id;
        }

        return (firstId * 4) + shown;
    }

    private void KeepOpen(string? draft, string key, bool error)
    {
        lock (_gate)
        {
            _open = true;
            _draft = draft ?? string.Empty;
        }

        this.AddSystem(key, null, error);
    }

    private void OnMessage(ChatLineInfo line) => this.Add(new Entry(0, 0, line, null, null, false));

    private void OnCompleted(ChatAction action, ChatStatus status, string nickname)
    {
        if (status != ChatStatus.Ok)
        {
            this.AddSystem("Chat_Status_" + status, null, error: true);
            return;
        }

        string? key = action switch
        {
            ChatAction.Block => "Chat_Sys_Blocked",
            ChatAction.Unblock => "Chat_Sys_Unblocked",
            ChatAction.Report => "Chat_Sys_Reported",
            _ => null,
        };

        if (key is not null)
        {
            this.AddSystem(key, nickname, error: false);
        }
    }

    private void AddSystem(string key, string? nickname, bool error) => this.Add(new Entry(0, 0, null, key, nickname, error));

    private void Add(Entry template)
    {
        lock (_gate)
        {
            _lines.Add(template with { Id = _nextId++, AtMs = _clock() });
            if (_lines.Count > MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            }

            _stamp = this.StampLocked(_clock());
        }

        this.Raise();
    }

    private ChatLineView Render(Entry entry)
    {
        if (entry.Message is { } line)
        {
            bool whisper = line.Channel == ChatChannel.Whisper;
            string from = _names.Display(line.From);
            string sender = whisper ? this.Text("Chat_WhisperFromTo", from, _names.Display(line.To)) : from;
            return new ChatLineView(entry.Id, whisper ? ChatLineKind.Whisper : ChatLineKind.Chat, line.Channel, this.Text("Chat_Channel_" + line.Channel), sender, line.From, line.Text);
        }

        string text = entry.Argument is { } nickname ? this.Text(entry.Key!, _names.Display(nickname)) : this.Text(entry.Key!);
        return new ChatLineView(entry.Id, entry.IsError ? ChatLineKind.Error : ChatLineKind.System, ChatChannel.Local, string.Empty, string.Empty, string.Empty, text);
    }

    private sealed record Entry(long Id, long AtMs, ChatLineInfo? Message, string? Key, string? Argument, bool IsError);
}
