// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Friends;

/// <summary>Outcome code of a friends request. Values are append-only.</summary>
public enum FriendStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,
    /// <summary>The nickname is not a valid nickname.</summary>
    InvalidNickname = 1,
    /// <summary>No player has that nickname (or the player cannot be reached).</summary>
    UnknownPlayer = 2,
    /// <summary>The player named themselves.</summary>
    Self = 3,
    /// <summary>A guest keeps no friends list; register first.</summary>
    GuestNotAllowed = 4,
    /// <summary>The two are friends already.</summary>
    AlreadyFriends = 5,
    /// <summary>A request to that player is open already.</summary>
    AlreadyRequested = 6,
    /// <summary>The friends list is full.</summary>
    ListFull = 7,
    /// <summary>There is no such request to answer.</summary>
    NoRequest = 8,
    /// <summary>The two are not friends.</summary>
    NotFriends = 9,
    /// <summary>Too many open requests.</summary>
    PendingFull = 10,
    /// <summary>Another change got there first; try again.</summary>
    Conflict = 11,
}
