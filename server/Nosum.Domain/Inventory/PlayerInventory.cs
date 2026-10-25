// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Inventory;

/// <summary>
/// An inventory header: how many slots it has and which hotbar slot is held. The stacks are <see cref="InventorySlot"/> rows. The owner is a player, or a
/// container such as a chest (docs/adr/ADR-024 D4): one table for every inventory owner, not a second store.
/// </summary>
public sealed class PlayerInventory
{
    /// <summary>Gets or sets the owner id: a player's account id, or a container's id from <c>ContainerIds</c>; also this row's primary key (one inventory per owner).</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the number of slots.</summary>
    public int Capacity { get; set; }

    /// <summary>Gets or sets the hotbar slot the player holds.</summary>
    public int SelectedSlot { get; set; }

    /// <summary>Gets or sets when the inventory was created (and given the starter kit).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
