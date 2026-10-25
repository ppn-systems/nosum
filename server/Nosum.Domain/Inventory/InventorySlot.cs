// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Inventory;

/// <summary>
/// One occupied inventory slot. Empty slots have no row. The key is (player, slot): a slot is
/// addressed by position and never has an identity of its own.
/// </summary>
public sealed class InventorySlot
{
    /// <summary>Gets or sets the owner of the inventory this slot is in: a player, or a container such as a chest.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the slot number, zero based.</summary>
    public int Slot { get; set; }

    /// <summary>Gets or sets the item id from the item catalogue.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets how many items are stacked (at least one).</summary>
    public int Quantity { get; set; }
}
