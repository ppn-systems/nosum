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

/// <summary>An owner's visiting settings. A row exists only once the owner changed a default; no row means every default.</summary>
public sealed class VisitSettings
{
    /// <summary>Gets or sets the owner.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets whether members of the owner's co-op may visit without being on the allow-list.</summary>
    public bool CoopMembersMayVisit { get; set; } = true;

    /// <summary>Gets or sets whether friends (docs/adr/ADR-029 D4) may visit without being on the allow-list.</summary>
    public bool FriendsMayVisit { get; set; } = true;

    /// <summary>Gets or sets when the row last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
