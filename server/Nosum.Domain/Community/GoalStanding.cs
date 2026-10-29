// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Domain.Community;

/// <summary>Where a goal of one season is in its life.</summary>
public enum GoalStanding
{
    /// <summary>Open: the season runs and the target is not reached.</summary>
    Active = 1,

    /// <summary>The target was reached. Permanent.</summary>
    Completed = 2,

    /// <summary>The season ended before the target was reached.</summary>
    Expired = 3,
}
