// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Shop;

/// <summary>Outcome codes of a shop purchase. Values are append-only.</summary>
public enum ShopStatus : byte
{
    /// <summary>Bought; the items are in the inventory and the coins are gone.</summary>
    Ok = 0,

    /// <summary>The shop id is not in the catalogue.</summary>
    UnknownShop = 1,

    /// <summary>The shop does not sell this item.</summary>
    NotSold = 2,

    /// <summary>The quantity is zero, negative or larger than a stack.</summary>
    InvalidQuantity = 3,

    /// <summary>The player is not on the map of the shop, or is too far from its counter.</summary>
    NotAtShop = 4,

    /// <summary>The player does not have the coins.</summary>
    NotEnoughCoins = 5,

    /// <summary>The inventory has no room for the items.</summary>
    NoSpace = 6,

    /// <summary>Another change happened at the same time; nothing was bought, ask again.</summary>
    Conflict = 7,

    /// <summary>The seed's crop does not grow in the current season, so the shop does not sell it now (docs/adr/ADR-018).</summary>
    OutOfSeason = 8,
}
