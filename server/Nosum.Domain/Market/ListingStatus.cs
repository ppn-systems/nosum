// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Domain.Market;

/// <summary>Where a listing is in its life.</summary>
public enum ListingStatus
{
    /// <summary>For sale; the items are in escrow (they are in no inventory).</summary>
    Active = 1,

    /// <summary>Bought; the items went to the buyer and the coins to the seller.</summary>
    Sold = 2,

    /// <summary>Withdrawn by the seller; the items went back.</summary>
    Cancelled = 3,

    /// <summary>Ran out of time; the items went back.</summary>
    Expired = 4,
}
