// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Blacksmith;

/// <summary>
/// A tool the blacksmith is upgrading for a player (docs/adr/ADR-026 D2). The old tool, the coins and the bars were taken when the row was
/// created; the row is deleted when the player collects the new tool. A player has at most one.
/// </summary>
public sealed class ToolUpgrade
{
    /// <summary>Gets or sets the player; also this row's primary key (one upgrade at a time).</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the item id of the tool the player gets back (<c>ItemCatalog</c>).</summary>
    public int ToolItemId { get; set; }

    /// <summary>Gets or sets the game day number from which the tool can be collected.</summary>
    public long ReadyDay { get; set; }

    /// <summary>Gets or sets when the tool was handed in.</summary>
    public DateTimeOffset StartedAt { get; set; }
}
