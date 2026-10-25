// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Inventory;

/// <summary>
/// One change to a player's item totals: the audit trail. Written in the same transaction as the
/// change. Moving a stack between slots does not change totals and is not logged.
/// </summary>
public sealed class InventoryLogEntry : Entity
{
    /// <summary>Gets or sets the player whose inventory changed.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets the change: positive when items were added, negative when removed.</summary>
    public int Delta { get; set; }

    /// <summary>Gets or sets why, a stable code such as <c>farm.harvest</c> or <c>starter</c> (never display text).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets when the change happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
