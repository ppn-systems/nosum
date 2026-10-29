// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Community;

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
