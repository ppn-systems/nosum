// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Visits;

/// <summary>An owner's permission for one player to visit their farm and farmhouse: one row of the owner's allow-list (docs/adr/ADR-019).</summary>
public sealed class VisitGrant
{
    /// <summary>Gets or sets the owner of the farm (the account that keeps the list).</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets the player allowed to visit.</summary>
    public Guid VisitorId { get; set; }

    /// <summary>Gets or sets when the owner added the player.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
