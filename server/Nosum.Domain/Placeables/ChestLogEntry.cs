// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Placeables;

/// <summary>
/// One item movement in or out of a chest: the audit trail, in the style of the co-op warehouse log (docs/adr/ADR-014 D9). Written in the same transaction
/// as the movement. The log outlives the chest, so it holds the object id without a foreign key.
/// </summary>
public sealed class ChestLogEntry : Entity
{
    /// <summary>Gets or sets the id of the placed chest.</summary>
    public int ObjectId { get; set; }

    /// <summary>Gets or sets the player who moved the items.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets the change of the chest's stock: positive when items were put in, negative when taken out.</summary>
    public int Delta { get; set; }

    /// <summary>Gets or sets why, a stable code: <c>chest.deposit</c> or <c>chest.withdraw</c> (never display text).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets when the movement happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
