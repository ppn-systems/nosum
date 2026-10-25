// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Friends;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Friends;

/// <summary>Friends list, requests and the friends-may-visit setting (docs/adr/ADR-029 D4). The identity comes from the connection; every outcome is the service's.</summary>
[PacketHandler("Nosum.Friends")]
public static partial class FriendHandlers
{
    [Inject] private static IFriendService s_friends = null!;

    /// <summary>Replies with the player's friends, requests and visiting setting.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FriendsOpCode.GetFriends)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "friends-read")]
    public static async ValueTask HandleGetFriendsAsync(IPacketContext<GetFriends> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        FriendsView view = await s_friends.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetFriends, FriendsSnapshot>(context, sequence, view.FillSnapshot).ConfigureAwait(false);
    }

    /// <summary>Sends a friend request by nickname.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FriendsOpCode.RequestFriend)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(2, 3, PolicyId = "friends-write")]
    public static async ValueTask HandleRequestFriendAsync(IPacketContext<RequestFriend> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        PlayerIdentity player = context.Connection.GetPlayer();

        FriendStatus status = await s_friends.RequestAsync(player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<RequestFriend, FriendResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Accepts or declines a friend request.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FriendsOpCode.RespondFriend)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 3, PolicyId = "friends-write")]
    public static async ValueTask HandleRespondFriendAsync(IPacketContext<RespondFriend> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid from = context.Packet.PlayerId;
        bool accept = context.Packet.Accept;
        PlayerIdentity player = context.Connection.GetPlayer();

        FriendStatus status = await s_friends.RespondAsync(player.PlayerId, from, accept, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<RespondFriend, FriendResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Removes a friend or withdraws a request.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FriendsOpCode.RemoveFriend)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 3, PolicyId = "friends-write")]
    public static async ValueTask HandleRemoveFriendAsync(IPacketContext<RemoveFriend> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid other = context.Packet.PlayerId;
        PlayerIdentity player = context.Connection.GetPlayer();

        FriendStatus status = await s_friends.RemoveAsync(player.PlayerId, other, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<RemoveFriend, FriendResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Turns "friends may visit" on or off.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FriendsOpCode.SetFriendsVisit)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(2, 3, PolicyId = "friends-write")]
    public static async ValueTask HandleSetFriendsVisitAsync(IPacketContext<SetFriendsVisit> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool allowed = context.Packet.Allowed;
        PlayerIdentity player = context.Connection.GetPlayer();

        FriendStatus status = await s_friends.SetFriendsMayVisitAsync(player.PlayerId, allowed, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<SetFriendsVisit, FriendResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
