// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Fishing;

/// <summary>Outcome codes of casting and reeling in (docs/adr/ADR-071). Values are append-only.</summary>
public enum FishingStatus : byte
{
    /// <summary>A cast is open (<see cref="S2C.CastResult"/>), or a fish was caught (<see cref="S2C.ReelResult"/>).</summary>
    Ok = 0,

    /// <summary>The line came up with nothing but weeds; the cast is over.</summary>
    Nothing = 1,

    /// <summary>The fish has not bitten yet; the cast stays open, reel in again when the window opens.</summary>
    TooEarly = 2,

    /// <summary>The fish got away: the press came after the window; the cast is over.</summary>
    Escaped = 3,

    /// <summary>The player does not hold a fishing rod.</summary>
    NoRod = 4,

    /// <summary>The tile is not water, or the map has no water to fish in.</summary>
    NotWater = 5,

    /// <summary>There is no open cast with that id (it was never made, is over, or belongs to someone else).</summary>
    NoCast = 6,

    /// <summary>The player is too far from the tile to cast there.</summary>
    TooFar = 7,

    /// <summary>The player does not have the energy for a cast.</summary>
    OutOfEnergy = 8,

    /// <summary>The inventory has no room for the catch.</summary>
    NoSpace = 9,

    /// <summary>Another change got in the way; nothing happened, try again.</summary>
    Conflict = 10,
}
