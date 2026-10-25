// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Goals;

/// <summary>Outcome code of a delivery to a community goal. Clients map it to text; no text travels on the wire. Values are append-only.</summary>
public enum GoalStatus : byte
{
    /// <summary>Delivered.</summary>
    Ok = 0,

    /// <summary>The goal id is not in the catalogue.</summary>
    UnknownGoal = 1,

    /// <summary>The quantity is zero, negative or larger than one delivery may hold.</summary>
    InvalidQuantity = 2,

    /// <summary>The goal is not open this season (its season is not the current one).</summary>
    NotThisSeason = 3,

    /// <summary>The goal is already complete, or its season ended.</summary>
    Closed = 4,

    /// <summary>The player is not at the Town board.</summary>
    NotAtBoard = 5,

    /// <summary>The player does not have that many of the item.</summary>
    NotEnough = 6,

    /// <summary>Another change happened at the same time; nothing was delivered, ask again.</summary>
    Conflict = 7,
}

/// <summary>Where a goal is in its life. Values are append-only.</summary>
public enum GoalState : byte
{
    /// <summary>Open: the season is running and the target is not reached.</summary>
    Active = 0,

    /// <summary>The target was reached; the reward was paid. Permanent.</summary>
    Completed = 1,

    /// <summary>The season ended first; the contributors got a consolation.</summary>
    Expired = 2,
}
