// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Requests;

/// <summary>Why a request delivery was or was not accepted. Values are append-only.</summary>
public enum RequestStatus : byte
{
    /// <summary>Delivered: the items were taken and the coins paid.</summary>
    Ok = 0,

    /// <summary>The slot is not a request of the day.</summary>
    UnknownRequest = 1,

    /// <summary>The day is not the current game day (the board was left open over midnight).</summary>
    NotToday = 2,

    /// <summary>The player already filled this request today.</summary>
    AlreadyFilled = 3,

    /// <summary>The player is not at the Town board.</summary>
    NotAtBoard = 4,

    /// <summary>The player does not carry that many of the item.</summary>
    NotEnough = 5,

    /// <summary>The coins would not fit in the purse.</summary>
    PurseFull = 6,

    /// <summary>Another change happened at the same time; nothing was delivered, ask again.</summary>
    Conflict = 7,
}
