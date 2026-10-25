// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Friends;

/// <summary>
/// Opcodes of the Friends domain.
/// <para>Friends list, requests and friends-may-visit (docs/adr/ADR-029 D4). Sub-range <c>0x9200-0x92FF</c> of the Trade band is reserved for this domain (docs/adr/ADR-029 D6). C2S requests use <c>0x9200-0x923F</c>, S2C replies and pushes <c>0x9240-0x927F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class FriendsOpCode
{
    // -- C2S --

    /// <summary>Asks for the friends list. Reply: <see cref="S2C.FriendsSnapshot"/>.</summary>
    public const ushort GetFriends = 0x9200;

    /// <summary>Sends a friend request by nickname. Reply: <see cref="S2C.FriendResult"/>.</summary>
    public const ushort RequestFriend = 0x9201;

    /// <summary>Accepts or declines a friend request. Reply: <see cref="S2C.FriendResult"/>.</summary>
    public const ushort RespondFriend = 0x9202;

    /// <summary>Removes a friend. Reply: <see cref="S2C.FriendResult"/>.</summary>
    public const ushort RemoveFriend = 0x9203;

    /// <summary>Turns friends may visit on or off. Reply: <see cref="S2C.FriendResult"/>.</summary>
    public const ushort SetFriendsVisit = 0x9204;

    // -- S2C --

    /// <summary>Reply to every friends request except <c>GetFriends</c>.</summary>
    public const ushort FriendResult = 0x9240;

    /// <summary>The friends list; reply to <c>GetFriends</c> and pushed to the players a change concerns.</summary>
    public const ushort FriendsSnapshot = 0x9241;

    /// <summary>Push to a player: a friend went online or offline or changed public map.</summary>
    public const ushort FriendStatusChanged = 0x9242;
}
