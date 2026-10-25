// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Player;

/// <summary>Outcome code of an <see cref="C2S.Eat"/> request. Clients map it to text. Values are append-only.</summary>
public enum EatStatus : byte
{
    /// <summary>One item was eaten and its energy restored.</summary>
    Ok = 0,

    /// <summary>The slot is outside the hotbar.</summary>
    InvalidSlot = 1,

    /// <summary>The slot is empty or holds something that cannot be eaten.</summary>
    NotEdible = 2,

    /// <summary>The food was no longer in the slot when it was eaten.</summary>
    ItemMissing = 3,

    /// <summary>Energy is full already; nothing was eaten.</summary>
    EnergyFull = 4,

    /// <summary>Another change was applied at the same time; ask again.</summary>
    Conflict = 5,
}
