// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

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

/// <summary>
/// The progress of one community goal in one season (docs/adr/ADR-030 D2): the total delivered by everybody. One row per goal id and season number;
/// the contributions are separate rows.
/// </summary>
public sealed class CommunityGoalProgress : Entity
{
    /// <summary>Gets or sets the goal id (<c>CommunityGoalCatalog</c>).</summary>
    public int GoalId { get; set; }

    /// <summary>Gets or sets the running season number the row belongs to (<c>GameCalendar.SeasonNumberAt</c>).</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets how many items everybody delivered. Also a concurrency token: two deliveries cannot both write the same total.</summary>
    public long Total { get; set; }

    /// <summary>Gets or sets the standing. Also a concurrency token: completing and expiring cannot both win.</summary>
    public GoalStanding Status { get; set; }

    /// <summary>Gets or sets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the goal was completed or expired.</summary>
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>What one player delivered to one goal row, and what they were paid for it once the goal closed.</summary>
public sealed class CommunityContribution : Entity
{
    /// <summary>Gets or sets the goal row (the id of a <see cref="CommunityGoalProgress"/>).</summary>
    public Guid ProgressId { get; set; }

    /// <summary>Gets or sets the contributor.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets how many items the player delivered.</summary>
    public long Delivered { get; set; }

    /// <summary>Gets or sets the coins paid to the player when the goal closed; <see langword="null"/> until paid. A payout runs only while this is <see langword="null"/>, which is what makes it happen once.</summary>
    public long? PaidCoins { get; set; }
}

/// <summary>A permanent unlock flag set by a completed goal (docs/adr/ADR-030 D1), asked by other features through <c>ICommunityUnlocks</c>.</summary>
public sealed class CommunityUnlockRecord
{
    /// <summary>Gets or sets the unlock key (<c>CommunityUnlockKeys</c>); also this row's primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the goal that set the flag.</summary>
    public int GoalId { get; set; }

    /// <summary>Gets or sets when the flag was set.</summary>
    public DateTimeOffset UnlockedAt { get; set; }
}
