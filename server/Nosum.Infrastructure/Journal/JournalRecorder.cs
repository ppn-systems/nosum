// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Domain.Journal;

namespace Nosum.Infrastructure.Journal;

/// <summary>
/// Writes the collection journal of one player inside a caller's unit of work. Only <c>InventoryService</c> calls it, from the transaction
/// that granted the items, so a discovery is saved if and only if the items are. The caller holds the player's inventory lock, which serializes the
/// count of one player; the primary key (player, item) is the backstop.
/// </summary>
internal static class JournalRecorder
{
    /// <summary>Counts the obtained items of one player into their journal, creating the entries of items not yet discovered.</summary>
    /// <param name="unit">The caller's unit of work; the rows are saved with it.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="obtained">The quantity obtained per item id, all positive (see <see cref="JournalRules.ObtainedTotals"/>).</param>
    /// <param name="obtainedAt">When the items were obtained; the first-obtained time of an item new to the journal.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when at least one row was added or changed.</returns>
    public static async Task<bool> RecordAsync(IUnitOfWork unit, Guid playerId, IReadOnlyDictionary<int, int> obtained, DateTimeOffset obtainedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(obtained);

        if (obtained.Count == 0)
        {
            return false;
        }

        int[] itemIds = [.. obtained.Keys];
        IRepository<PlayerDiscovery> rows = unit.Repository<PlayerDiscovery>();
        List<PlayerDiscovery> existing = await rows.ListAsync(d => d.PlayerId == playerId && itemIds.Contains(d.ItemId), cancellationToken).ConfigureAwait(false);
        Dictionary<int, PlayerDiscovery> byItem = existing.ToDictionary(d => d.ItemId);

        foreach (KeyValuePair<int, int> item in obtained)
        {
            if (byItem.TryGetValue(item.Key, out PlayerDiscovery? row))
            {
                row.Obtain(item.Value);
            }
            else
            {
                rows.Add(PlayerDiscovery.Start(playerId, item.Key, item.Value, obtainedAt));
            }
        }

        return true;
    }
}
