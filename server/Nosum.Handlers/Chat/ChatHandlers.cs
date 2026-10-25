// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Chat;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Chat;

/// <summary>
/// Chat, block list and reports (docs/adr/ADR-029 D1-D3). The identity comes from the connection; the service cleans and masks the text, applies
/// the rate rule and the block lists and decides who receives it. The text is encrypted on the wire like a trade offer: whispers are private.
/// </summary>
[PacketHandler("Nosum.Chat")]
public static partial class ChatHandlers
{
    [Inject] private static IChatService s_chat = null!;
    [Inject] private static IBlockList s_blocks = null!;

    /// <summary>Sends a chat message. The transport throttles bursts; the exact rule (5 messages per 10 seconds, burst 3) is the service's.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ChatOpCode.SendChat)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 3, PolicyId = "chat-send")]
    public static async ValueTask HandleSendChatAsync(IPacketContext<SendChat> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        ChatChannel channel = context.Packet.Channel;
        string target = context.Packet.Target;
        string text = context.Packet.Text;
        IConnection connection = context.Connection;
        PlayerIdentity player = connection.GetPlayer();

        ChatStatus status = await s_chat.SendAsync(connection, player.PlayerId, channel, target, text, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<SendChat, ChatResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Adds a player to the block list.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ChatOpCode.BlockPlayer)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 3, PolicyId = "chat-block")]
    public static async ValueTask HandleBlockPlayerAsync(IPacketContext<BlockPlayer> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        PlayerIdentity player = context.Connection.GetPlayer();

        ChatStatus status = await s_blocks.BlockAsync(player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<BlockPlayer, ChatResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Takes a player off the block list.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ChatOpCode.UnblockPlayer)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 3, PolicyId = "chat-block")]
    public static async ValueTask HandleUnblockPlayerAsync(IPacketContext<UnblockPlayer> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        PlayerIdentity player = context.Connection.GetPlayer();

        ChatStatus status = await s_blocks.UnblockAsync(player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<UnblockPlayer, ChatResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Replies with the player's block list.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ChatOpCode.GetBlocked)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "chat-read")]
    public static async ValueTask HandleGetBlockedAsync(IPacketContext<GetBlocked> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        IReadOnlyList<BlockedView> blocked = await s_blocks.ListAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetBlocked, BlockedList>(context, sequence, list =>
        {
            list.Players.Clear();
            foreach (BlockedView view in blocked)
            {
                BlockedRow row = BlockedRow.Create();
                row.PlayerId = view.PlayerId;
                row.Nickname = view.Nickname;
                list.Players.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Reports a player; the last messages the reporter received are stored for the admins.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ChatOpCode.ReportChat)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(1, 3, PolicyId = "chat-report")]
    public static async ValueTask HandleReportChatAsync(IPacketContext<ReportChat> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        PlayerIdentity player = context.Connection.GetPlayer();

        ChatStatus status = await s_chat.ReportAsync(player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<ReportChat, ChatResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
