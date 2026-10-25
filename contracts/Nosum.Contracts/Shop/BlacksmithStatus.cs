// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Shop;

/// <summary>Outcome codes of a blacksmith request (docs/adr/ADR-026 D2). Values are append-only.</summary>
public enum BlacksmithStatus : byte
{
    /// <summary>Done (or, for a read, here is the state).</summary>
    Ok = 0,

    /// <summary>The player is not standing at the blacksmith's counter.</summary>
    NotAtCounter = 1,

    /// <summary>The item is not a tool that can be upgraded, or it is already the best tier.</summary>
    NotUpgradable = 2,

    /// <summary>The blacksmith already holds a tool of this player; one upgrade at a time.</summary>
    Busy = 3,

    /// <summary>The player does not have that tool.</summary>
    NoTool = 4,

    /// <summary>The player does not have the coins.</summary>
    NotEnoughCoins = 5,

    /// <summary>The player does not have the bars.</summary>
    NotEnoughBars = 6,

    /// <summary>The inventory has no room for the tool.</summary>
    NoSpace = 7,

    /// <summary>The blacksmith holds no tool of this player.</summary>
    NothingToCollect = 8,

    /// <summary>The upgrade is not finished yet.</summary>
    NotReady = 9,

    /// <summary>Another change happened at the same time; nothing changed, ask again.</summary>
    Conflict = 10,
}
