// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Domain.Journal;

/// <summary>The pure part of the collection journal: which items a set of inventory changes obtained.</summary>
public static class JournalRules
{
    /// <summary>
    /// Totals the items a change obtained: the sum of the positive changes per item. Removals are not obtaining, and a move or a sort changes no
    /// total at all, so it passes no changes. Items with no positive change are left out.
    /// </summary>
    /// <param name="changes">The item changes of one player's inventory: a positive delta is items gained, a negative one items lost.</param>
    /// <returns>The obtained quantity per item id, in the order the items first appeared.</returns>
    public static Dictionary<int, int> ObtainedTotals(IEnumerable<(int ItemId, int Delta)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        Dictionary<int, int> totals = [];
        foreach ((int itemId, int delta) in changes)
        {
            if (delta > 0)
            {
                totals[itemId] = (int)Math.Min(int.MaxValue, (long)totals.GetValueOrDefault(itemId) + delta);
            }
        }

        return totals;
    }
}
