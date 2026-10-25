// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Chat;

/// <summary>Outcome code of a chat request. Values are append-only.</summary>
public enum ChatStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,
    /// <summary>Nothing is left of the text after cleaning.</summary>
    Empty = 1,
    /// <summary>The player is on no map.</summary>
    NotOnMap = 2,
    /// <summary>A guest may only use local chat.</summary>
    GuestLocalOnly = 3,
    /// <summary>The player is in no co-op.</summary>
    NotInCoop = 4,
    /// <summary>No player of that name is online (or the player cannot be reached).</summary>
    TargetOffline = 5,
    /// <summary>A player cannot whisper themselves.</summary>
    SelfWhisper = 6,
    /// <summary>The nickname is not a valid nickname.</summary>
    InvalidNickname = 7,
    /// <summary>The player blocked the addressee; unblock first.</summary>
    Blocked = 8,
    /// <summary>Too many messages; wait a moment.</summary>
    RateLimited = 9,
    /// <summary>No player has that nickname.</summary>
    UnknownPlayer = 10,
    /// <summary>The player is already on the block list.</summary>
    AlreadyBlocked = 11,
    /// <summary>The player is not on the block list.</summary>
    NotBlocked = 12,
    /// <summary>The block list is full.</summary>
    BlockListFull = 13,
    /// <summary>The channel is not one of the known channels.</summary>
    InvalidChannel = 14,
    /// <summary>No recent message from that player to report.</summary>
    NothingToReport = 15,
    /// <summary>A player cannot block or report themselves.</summary>
    SelfBlock = 16,
}
