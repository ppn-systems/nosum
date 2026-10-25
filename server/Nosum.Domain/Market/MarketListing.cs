// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

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

/// <summary>One item stack on the consignment market, held in escrow while it is active (docs/adr/ADR-013).</summary>
public sealed class MarketListing : Entity
{
    /// <summary>Gets or sets the seller.</summary>
    public Guid SellerId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets how many items are in the stack.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price for the whole stack, coins.</summary>
    public long Price { get; set; }

    /// <summary>Gets or sets the status. Also a concurrency token: of two writers closing the same listing only one saves.</summary>
    public ListingStatus Status { get; set; }

    /// <summary>Gets or sets when the listing was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the listing expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Gets or sets the buyer, once sold.</summary>
    public Guid? BuyerId { get; set; }

    /// <summary>Gets or sets when the listing was sold, cancelled or expired.</summary>
    public DateTimeOffset? ClosedAt { get; set; }
}
