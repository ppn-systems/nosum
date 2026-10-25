// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Inventory;

/// <summary>Outcome code of an inventory request. Clients map it to text; no text travels on the wire. Values are append-only.</summary>
public enum InventoryStatus : byte
{
    /// <summary>The request was applied.</summary>
    Ok = 0,

    /// <summary>A slot number is outside the inventory (or outside the hotbar for a selection).</summary>
    InvalidSlot = 1,

    /// <summary>The source slot holds nothing.</summary>
    EmptySlot = 2,

    /// <summary>A quantity is zero, negative or larger than the stack.</summary>
    InvalidQuantity = 3,

    /// <summary>The item id is not in the catalogue.</summary>
    UnknownItem = 4,

    /// <summary>The player has fewer of the item than the request needs.</summary>
    NotEnough = 5,

    /// <summary>There is no room for the item.</summary>
    NoSpace = 6,

    /// <summary>The target slot holds another item and part of a stack cannot swap with it.</summary>
    Blocked = 7,

    /// <summary>Another change to the same data was applied at the same time; ask again.</summary>
    Conflict = 8,
}
