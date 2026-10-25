// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Inventory;

/// <summary>A player's inventory header: how many slots it has and which hotbar slot is held. The stacks are <see cref="InventorySlot"/> rows.</summary>
public sealed class PlayerInventory
{
    /// <summary>Gets or sets the owning player; also this row's primary key (one inventory per player).</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the number of slots.</summary>
    public int Capacity { get; set; }

    /// <summary>Gets or sets the hotbar slot the player holds.</summary>
    public int SelectedSlot { get; set; }

    /// <summary>Gets or sets when the inventory was created (and given the starter kit).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
