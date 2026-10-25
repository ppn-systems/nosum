// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>Outcome code of a craft request. Clients map it to text. Values are append-only.</summary>
public enum CraftStatus : byte
{
    /// <summary>The items were made.</summary>
    Ok = 0,

    /// <summary>No recipe has that id.</summary>
    UnknownRecipe = 1,

    /// <summary>The number of crafts is not 1 to 99.</summary>
    InvalidQuantity = 2,

    /// <summary>The player's skill level does not unlock the recipe yet.</summary>
    Locked = 3,

    /// <summary>The player does not have the items the recipe consumes.</summary>
    NotEnough = 4,

    /// <summary>The inventory has no room for what the recipe makes.</summary>
    NoSpace = 5,

    /// <summary>Another change to the same inventory was applied at the same time; ask again.</summary>
    Conflict = 6,
}
