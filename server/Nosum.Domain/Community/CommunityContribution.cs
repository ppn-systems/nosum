// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Community;

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
