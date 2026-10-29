// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Coops;

/// <summary>How many of one item kind the warehouse holds. A warehouse is stock by item, not slots; a row exists only while the quantity is positive.</summary>
public sealed class CoopStockItem
{
    /// <summary>Gets or sets the co-op.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets the quantity (at least one).</summary>
    public int Quantity { get; set; }
}
