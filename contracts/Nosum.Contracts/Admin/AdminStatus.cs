// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Admin;

/// <summary>Outcome code of an admin intent. Codes only, never display text. Values are append-only.</summary>
public enum AdminStatus : byte
{
    /// <summary>The intent was carried out.</summary>
    Ok = 0,

    /// <summary>The signed-in account is not (or no longer) an admin.</summary>
    NotAdmin = 1,

    /// <summary>No player has that nickname.</summary>
    UnknownPlayer = 2,

    /// <summary>The nickname is not a valid nickname.</summary>
    InvalidNickname = 3,

    /// <summary>An admin cannot kick, mute or ban themselves.</summary>
    SelfTarget = 4,

    /// <summary>An admin cannot kick, mute or ban another admin; revoke the role from the host first.</summary>
    TargetIsAdmin = 5,

    /// <summary>The mute length is negative or above <see cref="AdminLimits.MaxMuteHours"/>.</summary>
    InvalidDuration = 6,

    /// <summary>The item id is not in the catalogue, or both the item and the coins are empty.</summary>
    InvalidItem = 7,

    /// <summary>The quantity or the coin amount is out of range.</summary>
    InvalidQuantity = 8,

    /// <summary>The player is not connected (kick).</summary>
    NotOnline = 9,

    /// <summary>No report has that id.</summary>
    UnknownReport = 10,

    /// <summary>The grant could not be delivered (inventory full or wallet limit).</summary>
    GrantFailed = 11,
}
