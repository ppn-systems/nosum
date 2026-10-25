// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Mine;

/// <summary>Outcome codes of a mine request (docs/adr/ADR-026 D3). Values are append-only.</summary>
public enum MineStatus : byte
{
    /// <summary>Done (or, for a read, here is the state).</summary>
    Ok = 0,

    /// <summary>The player is not standing at an elevator.</summary>
    NotAtElevator = 1,

    /// <summary>The stop is not one the elevator has, or the player has not been that deep.</summary>
    NotUnlocked = 2,

    /// <summary>The player is already on that floor.</summary>
    AlreadyThere = 3,

    /// <summary>The player cannot move right now (they are changing map).</summary>
    Busy = 4,
}
