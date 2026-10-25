// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Inventory;

/// <summary>Result of a rule on an <see cref="InventoryGrid"/>. A refusal changes nothing.</summary>
public enum InventoryVerdict
{
    /// <summary>The change was applied.</summary>
    Ok = 0,

    /// <summary>A slot number is outside the inventory.</summary>
    InvalidSlot = 1,

    /// <summary>The source slot holds nothing.</summary>
    EmptySlot = 2,

    /// <summary>A quantity is zero or negative, or larger than the stack.</summary>
    InvalidQuantity = 3,

    /// <summary>The item id is not in the catalogue.</summary>
    UnknownItem = 4,

    /// <summary>The inventory holds fewer of the item than asked.</summary>
    NotEnough = 5,

    /// <summary>There is no room for the item.</summary>
    NoSpace = 6,

    /// <summary>The target slot holds another item and a part of a stack cannot swap with it.</summary>
    Blocked = 7,
}
