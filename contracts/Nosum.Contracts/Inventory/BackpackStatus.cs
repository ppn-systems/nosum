// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Inventory;

/// <summary>Outcome code of a backpack request (docs/adr/ADR-081). Clients map it to text; no text travels on the wire. Values are append-only.</summary>
public enum BackpackStatus : byte
{
    /// <summary>Done (or, for a read, here is the state).</summary>
    Ok = 0,

    /// <summary>The player is not standing at the tailor's counter.</summary>
    NotAtCounter = 1,

    /// <summary>The backpack is already the largest size.</summary>
    MaxSize = 2,

    /// <summary>The player does not have the coins for the next size.</summary>
    NotEnoughCoins = 3,

    /// <summary>Another change happened at the same time; nothing changed, ask again.</summary>
    Conflict = 4,
}
