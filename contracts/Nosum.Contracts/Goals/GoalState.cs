// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Goals;

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
