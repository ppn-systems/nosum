// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Coops;

/// <summary>One deposit or withdrawal: the warehouse audit trail, written in the same transaction as the change.</summary>
public sealed class CoopLogEntry : Entity
{
    /// <summary>Gets or sets the co-op.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the membership id of whoever did it (not an account id).</summary>
    public Guid MemberId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets the change: positive for a deposit, negative for a withdrawal.</summary>
    public int Delta { get; set; }

    /// <summary>Gets or sets a stable code: <c>coop.deposit</c>, <c>coop.withdraw</c>, <c>coop.upgrade.*</c> (docs/adr/ADR-038) or a farm-land code (docs/adr/ADR-020).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets when it happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
