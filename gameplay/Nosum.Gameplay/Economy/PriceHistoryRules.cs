// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Gameplay.Economy;

/// <summary>The sales of one item on one game day, summarized (docs/adr/ADR-030 D4).</summary>
/// <param name="Min">The lowest unit price.</param>
/// <param name="Median">The middle unit price (of two middle prices, their mean rounded down).</param>
/// <param name="Max">The highest unit price.</param>
/// <param name="Sales">How many sales the day had.</param>
public readonly record struct PriceSummary(long Min, long Median, long Max, int Sales);

/// <summary>Pure rules of the per-item price history the market page shows (docs/adr/ADR-030 D4).</summary>
public static class PriceHistoryRules
{
    /// <summary>How many game days of history the market page asks for and the server returns at most.</summary>
    public const int HistoryDays = 14;

    /// <summary>How many game days the rolling average unit price looks back over (the newest day included).</summary>
    public const int AverageDays = 7;

    /// <summary>Prices a unit of a sold stack: the stack price divided by its quantity, rounded to the nearest coin (at least 1).</summary>
    /// <param name="stackPrice">The price of the whole stack.</param>
    /// <param name="quantity">How many items were in the stack.</param>
    /// <returns>The price of one unit.</returns>
    public static long UnitPrice(long stackPrice, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return Math.Max(1, (stackPrice + (quantity / 2)) / quantity);
    }

    /// <summary>Summarizes the unit prices of one day's sales.</summary>
    /// <param name="unitPrices">The unit prices, in any order; at least one.</param>
    /// <returns>The minimum, median and maximum.</returns>
    public static PriceSummary Summarize(IReadOnlyCollection<long> unitPrices)
    {
        ArgumentNullException.ThrowIfNull(unitPrices);
        ArgumentOutOfRangeException.ThrowIfZero(unitPrices.Count);

        long[] sorted = [.. unitPrices];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        long median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return new PriceSummary(sorted[0], median, sorted[^1], sorted.Length);
    }

    /// <summary>Averages the unit prices of some sales, rounded to the nearest coin (halves up).</summary>
    /// <param name="unitPrices">The unit prices, in any order; at least one.</param>
    /// <returns>The mean unit price.</returns>
    public static long Average(IReadOnlyCollection<long> unitPrices)
    {
        ArgumentNullException.ThrowIfNull(unitPrices);
        ArgumentOutOfRangeException.ThrowIfZero(unitPrices.Count);

        long sum = 0;
        foreach (long price in unitPrices)
        {
            sum += price;
        }

        return (sum + (unitPrices.Count / 2)) / unitPrices.Count;
    }
}
