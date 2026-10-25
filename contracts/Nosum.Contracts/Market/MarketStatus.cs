// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Market;

/// <summary>Outcome code of a coin or market request. Clients map it to text; no text travels on the wire. Values are append-only.</summary>
public enum MarketStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The item id is not in the catalogue.</summary>
    UnknownItem = 1,

    /// <summary>The item cannot be sold.</summary>
    NotTradable = 2,

    /// <summary>The quantity is zero, negative or too large.</summary>
    InvalidQuantity = 3,

    /// <summary>The price is outside the allowed range.</summary>
    InvalidPrice = 4,

    /// <summary>The player already has the most listings allowed.</summary>
    TooManyListings = 5,

    /// <summary>The player does not have the items.</summary>
    NotEnough = 6,

    /// <summary>The inventory has no room for the items.</summary>
    NoSpace = 7,

    /// <summary>The player does not have the coins.</summary>
    NotEnoughCoins = 8,

    /// <summary>The listing was sold, cancelled or never existed.</summary>
    ListingGone = 9,

    /// <summary>A player cannot buy their own listing.</summary>
    OwnListing = 10,

    /// <summary>The listing belongs to someone else.</summary>
    NotOwner = 11,

    /// <summary>The listing has expired.</summary>
    Expired = 12,

    /// <summary>Another change happened at the same time; ask again.</summary>
    Conflict = 13,

    /// <summary>The player is not standing at the shipping bin.</summary>
    NotAtBin = 14,

    /// <summary>The bin does not buy this item.</summary>
    NotShippable = 15,

    /// <summary>The slot is outside the inventory or holds something else.</summary>
    InvalidSlot = 16,

    /// <summary>The balance would exceed the largest allowed.</summary>
    TooBig = 17,

    /// <summary>The shipping bin belongs to the farm's owner; a visitor cannot use it (docs/adr/ADR-019 D3).</summary>
    NotYourBin = 18,

    /// <summary>The player would have more than 500 units of this item listed at once (docs/adr/ADR-030 D4).</summary>
    TooManyOfItem = 19,
}
