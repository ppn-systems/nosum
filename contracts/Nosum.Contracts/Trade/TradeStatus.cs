// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Trade;

/// <summary>Outcome code of a trade request. Values are append-only.</summary>
public enum TradeStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The player is on no map.</summary>
    NotOnMap = 1,

    /// <summary>The other player is not on the same map.</summary>
    TargetNotFound = 2,

    /// <summary>A player cannot trade with themselves.</summary>
    SelfTrade = 3,

    /// <summary>The player is already in a trade or has an invitation open.</summary>
    AlreadyTrading = 4,

    /// <summary>The other player is already in a trade or has an invitation open.</summary>
    TargetBusy = 5,

    /// <summary>There is no invitation to answer.</summary>
    NoInvite = 6,

    /// <summary>There is no open trade.</summary>
    NoSession = 7,

    /// <summary>The offer is malformed.</summary>
    InvalidOffer = 8,

    /// <summary>The player does not have the offered items.</summary>
    NotEnoughItems = 9,

    /// <summary>The player does not have the offered coins.</summary>
    NotEnoughCoins = 10,

    /// <summary>Both sides offer nothing.</summary>
    NothingOffered = 11,

    /// <summary>The offer changed too recently to confirm; retry when the lock in the trade state has passed.</summary>
    TooSoon = 12,
}
