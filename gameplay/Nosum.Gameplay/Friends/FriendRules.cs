// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Friends;

/// <summary>Limits of the friends list (docs/adr/ADR-029 D4). Pure data shared by the server and the client.</summary>
public static class FriendRules
{
    /// <summary>The most friends a player keeps.</summary>
    public const int MaxFriends = 100;

    /// <summary>The most open requests a player may have sent, and the most they may hold waiting.</summary>
    public const int MaxPending = 50;
}
