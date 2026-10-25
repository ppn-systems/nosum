// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Social;

/// <summary>One row of a player's block list: the owner does not want to hear from, trade with or be visited by the blocked player (docs/adr/ADR-029 D3).</summary>
public sealed class BlockedPlayer
{
    /// <summary>Gets or sets the player who keeps the list.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the blocked player.</summary>
    public Guid BlockedId { get; set; }

    /// <summary>Gets or sets when the owner blocked the player.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
