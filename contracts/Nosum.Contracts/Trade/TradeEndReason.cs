// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Trade;

/// <summary>Why a trade ended. Reasons are from the receiver's point of view. Values are append-only.</summary>
public enum TradeEndReason : byte
{
    /// <summary>The swap happened.</summary>
    Completed = 1,

    /// <summary>The receiver cancelled.</summary>
    Cancelled = 2,

    /// <summary>The other player cancelled.</summary>
    CancelledByOther = 3,

    /// <summary>The other player left the map or disconnected.</summary>
    OtherLeft = 4,

    /// <summary>The invitation was declined.</summary>
    Declined = 5,

    /// <summary>The invitation was not answered in time.</summary>
    InviteExpired = 6,

    /// <summary>A side no longer had its items; nothing moved.</summary>
    NotEnoughItems = 7,

    /// <summary>A side no longer had its coins; nothing moved.</summary>
    NotEnoughCoins = 8,

    /// <summary>A side had no room for the items; nothing moved.</summary>
    NoSpace = 9,

    /// <summary>The swap failed; nothing moved.</summary>
    Failed = 10,

    /// <summary>The receiver left the map.</summary>
    SelfLeft = 11,
}
