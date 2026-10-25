// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;

namespace Nosum.Wasm.Socket.Chat;

/// <summary>One chat line as the server pushed it. The text is player-authored: show it as plain text only.</summary>
/// <param name="Channel">The channel it was sent on.</param>
/// <param name="From">The sender's nickname as stored (a guest has the neutral <c>guest-1234</c> form).</param>
/// <param name="To">The addressee's nickname for a whisper; empty otherwise.</param>
/// <param name="Text">The cleaned, masked text.</param>
/// <param name="SentAtMs">Server time of sending, Unix milliseconds.</param>
public readonly record struct ChatLineInfo(ChatChannel Channel, string From, string To, string Text, long SentAtMs);

/// <summary>A player on the block list.</summary>
/// <param name="PlayerId">The player's account id.</param>
/// <param name="Nickname">The nickname as the server sent it.</param>
public readonly record struct BlockedPlayerInfo(Guid PlayerId, string Nickname);

/// <summary>The kind of chat request an answer belongs to.</summary>
public enum ChatAction
{
    /// <summary>A message was sent.</summary>
    Send = 0,

    /// <summary>A player was blocked.</summary>
    Block = 1,

    /// <summary>A player was unblocked.</summary>
    Unblock = 2,

    /// <summary>A player was reported.</summary>
    Report = 3,
}

/// <summary>
/// The chat side of the connection (docs/adr/ADR-029): sends messages and moderation requests, receives pushed lines and reads the block list.
/// The server cleans, filters, rate-limits and delivers every message and decides every outcome; the sender gets their own line back as a push.
/// </summary>
public sealed class ChatMessenger : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _messages;

    /// <summary>Initializes the channel and starts listening for pushed lines.</summary>
    /// <param name="socket">The game connection.</param>
    public ChatMessenger(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _messages = socket.Subscribe<ChatMessage>(p => this.Message?.Invoke(new ChatLineInfo(p.Channel, p.FromNickname, p.ToNickname, p.Text, p.SentAtMs)));
    }

    /// <summary>Raised for every pushed chat line. May run on any thread.</summary>
    public event Action<ChatLineInfo>? Message;

    /// <summary>Raised when the block list changed. May run on any thread.</summary>
    public event Action? BlockedChanged;

    /// <summary>Raised with the server's answer to a request: what was asked, the outcome, and the nickname it concerned (empty for a message). May run on any thread.</summary>
    public event Action<ChatAction, ChatStatus, string>? Completed;

    /// <summary>Gets the block list as the server last sent it.</summary>
    public IReadOnlyList<BlockedPlayerInfo> Blocked { get; private set; } = [];

    /// <summary>Sends a message.</summary>
    /// <param name="channel">The channel to send to.</param>
    /// <param name="target">The addressee's nickname for a whisper; empty for the other channels.</param>
    /// <param name="text">The text as typed.</param>
    public void Send(ChatChannel channel, string target, string text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        _ = this.SendAsync<SendChat>(ChatAction.Send, string.Empty, r =>
        {
            r.Channel = channel;
            r.Target = target;
            r.Text = text;
        });
    }

    /// <summary>Adds a player to the block list.</summary>
    /// <param name="nickname">Their nickname.</param>
    public void Block(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _ = this.SendAsync<BlockPlayer>(ChatAction.Block, nickname, r => r.Nickname = nickname);
    }

    /// <summary>Removes a player from the block list.</summary>
    /// <param name="nickname">Their nickname.</param>
    public void Unblock(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _ = this.SendAsync<UnblockPlayer>(ChatAction.Unblock, nickname, r => r.Nickname = nickname);
    }

    /// <summary>Reports a player for what they wrote; the server keeps the last messages this player received as context.</summary>
    /// <param name="nickname">Their nickname.</param>
    public void Report(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _ = this.SendAsync<ReportChat>(ChatAction.Report, nickname, r => r.Nickname = nickname);
    }

    /// <summary>Reads the block list.</summary>
    public void LoadBlocked() => _ = this.LoadBlockedAsync();

    /// <inheritdoc/>
    public void Dispose() => _messages.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the page opens again; there is no caller to report to.")]
    private async Task LoadBlockedAsync()
    {
        try
        {
            using PacketScope<GetBlocked> request = PacketFactory<GetBlocked>.Acquire();
            BlockedList list = await _socket.RequestAsync<BlockedList>(request.Value).ConfigureAwait(false);
            List<BlockedPlayerInfo> players = new(list.Players.Count);
            foreach (BlockedRow row in list.Players)
            {
                players.Add(new BlockedPlayerInfo(row.PlayerId, row.Nickname));
            }

            this.Blocked = players;
            this.BlockedChanged?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(ChatAction action, string nickname, Action<TRequest> fill)
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            ChatResult reply = await _socket.RequestAsync<ChatResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(action, reply.Status, nickname);
            if (reply.Status == ChatStatus.Ok && action is ChatAction.Block or ChatAction.Unblock)
            {
                await this.LoadBlockedAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
