// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Wasm.Socket.Coop;

namespace Nosum.Wasm.ViewModels.Coop;

/// <summary>How many items one member deposited into the co-op warehouse, counted from the log the server sent.</summary>
/// <param name="MemberId">The member; <see cref="Guid.Empty"/> for a member who has left the co-op.</param>
/// <param name="Items">The items deposited: the sum of the positive log deltas.</param>
public readonly record struct CoopContributionTotal(Guid MemberId, int Items);

/// <summary>
/// Totals the co-op warehouse log per member for the co-op page's "who contributed" section. Only deposits count (a positive delta). The log also holds the
/// stock an upgrade took (a negative delta), which is a cost paid for the co-op, not a gift, so it is left out. The server sends only its recent window of
/// the log, so the totals cover that window and not the co-op's whole history.
/// </summary>
public static class CoopContributions
{
    /// <summary>Totals the deposits per member, the biggest contributor first (ties by member id, so the order does not change between pushes).</summary>
    /// <param name="log">The warehouse log, in any order.</param>
    /// <returns>One row per member who deposited at least one item.</returns>
    public static List<CoopContributionTotal> Summarize(IReadOnlyList<CoopLogInfo> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Dictionary<Guid, int> totals = [];
        foreach (CoopLogInfo entry in log)
        {
            if (entry.Delta <= 0)
            {
                continue;
            }

            totals.TryGetValue(entry.MemberId, out int items);
            totals[entry.MemberId] = items + entry.Delta;
        }

        List<CoopContributionTotal> rows = new(totals.Count);
        foreach (KeyValuePair<Guid, int> pair in totals)
        {
            rows.Add(new CoopContributionTotal(pair.Key, pair.Value));
        }

        rows.Sort((a, b) =>
        {
            int byItems = b.Items.CompareTo(a.Items);
            return byItems != 0 ? byItems : a.MemberId.CompareTo(b.MemberId);
        });
        return rows;
    }
}
