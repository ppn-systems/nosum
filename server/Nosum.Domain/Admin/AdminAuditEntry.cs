// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Admin;

/// <summary>One thing an admin did to a player (docs/adr/ADR-031 D7). Rows are only ever added.</summary>
public sealed class AdminAuditEntry : Entity
{
    /// <summary>Gets or sets the admin who acted; null when the command ran from the host's command line.</summary>
    public Guid? AdminId { get; set; }

    /// <summary>Gets or sets the action (an <c>AdminAction</c> value, append-only).</summary>
    public byte Action { get; set; }

    /// <summary>Gets or sets the player the action was about; null when it was about no player.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Gets or sets a short machine-readable detail such as <c>hours=4</c> or <c>item=12 qty=5</c>; never chat text or secrets.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>Gets or sets when the action happened.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
