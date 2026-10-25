// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Market;

/// <summary>One sale on the consignment market, kept for the price history of its item (docs/adr/ADR-030 D4).</summary>
public sealed class MarketSale : Entity
{
    /// <summary>Gets or sets the item that was sold.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets how many items the stack held.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the price of the whole stack, coins.</summary>
    public long Price { get; set; }

    /// <summary>Gets or sets the price of one unit, coins (<c>PriceHistoryRules.UnitPrice</c>).</summary>
    public long UnitPrice { get; set; }

    /// <summary>Gets or sets the game day number of the sale (<c>GameCalendar.DayNumberAt</c>).</summary>
    public int Day { get; set; }

    /// <summary>Gets or sets when the item was sold.</summary>
    public DateTimeOffset SoldAt { get; set; }
}
