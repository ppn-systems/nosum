// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Coop;
using Nosum.Domain.Common;

namespace Nosum.Domain.Coops;

/// <summary>A co-operative: a named group of players with a shared warehouse (docs/adr/ADR-014).</summary>
public sealed class Coop : Entity
{
    /// <summary>Gets or sets the name as the founder typed it: plain text or an <c>i18n{</c> envelope (docs/adr/ADR-010 D5).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets when the co-op was founded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A player's membership of a co-op. A player belongs to at most one co-op (unique player id).</summary>
public sealed class CoopMember : Entity
{
    /// <summary>Gets or sets the co-op.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the member's account.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the member's role.</summary>
    public CoopRole Role { get; set; }

    /// <summary>Gets or sets when the player joined.</summary>
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>An open invitation of a player to a co-op.</summary>
public sealed class CoopInvite : Entity
{
    /// <summary>Gets or sets the co-op that invites.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the invited player.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets when the invitation was made.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the invitation expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>How many of one item kind the warehouse holds. A warehouse is stock by item, not slots; a row exists only while the quantity is positive.</summary>
public sealed class CoopStockItem
{
    /// <summary>Gets or sets the co-op.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets the quantity (at least one).</summary>
    public int Quantity { get; set; }
}

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

    /// <summary>Gets or sets a stable code, <c>coop.deposit</c> or <c>coop.withdraw</c>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets when it happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
