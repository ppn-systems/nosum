// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Domain.Journal;
using Nosum.Infrastructure.Caching;

namespace Nosum.Infrastructure.Journal;

/// <summary>
/// Counts the items a player obtained into their collection journal, inside the caller's unit of work. Only <c>InventoryService</c> calls it, from the
/// transaction that granted the items, so a discovery is saved if and only if the items are. The caller holds the player's inventory lock, which serializes
/// the count of one player; the primary key (player, item) is the backstop.
/// <para>
/// Docs/adr/ADR-035 D5: the discovered items of a player are kept in memory once they are read, so a grant of an item the player already has costs no read,
/// only the count update. A change is kept only after its save succeeded (<see cref="Commit"/>); any failure drops the player's entry (<see cref="Forget"/>),
/// and the next grant reads the rows again. A brand-new inventory has no discoveries yet, so its first grant reads nothing.
/// </para>
/// </summary>
internal sealed class JournalRecorder
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ReadThroughCache<Guid, Dictionary<int, Tally>> _shelves = new();

    /// <summary>Initializes the recorder.</summary>
    /// <param name="unitOfWork">Creates the units of work that read a player's journal the first time it is needed.</param>
    public JournalRecorder(IUnitOfWorkFactory unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _unitOfWork = unitOfWork;
    }

    /// <summary>Plans the journal rows of one grant and adds them to the caller's unit of work.</summary>
    /// <param name="unit">The caller's unit of work; the rows are saved with it.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="obtained">The quantity obtained per item id, all positive (see <see cref="JournalRules.ObtainedTotals"/>).</param>
    /// <param name="obtainedAt">When the items were obtained; the first-obtained time of an item new to the journal.</param>
    /// <param name="isNew">Whether the player's inventory was created by this grant, which means the journal is empty.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The journal as it will be once saved, to pass to <see cref="Commit"/>; <see langword="null"/> when nothing was obtained.</returns>
    public async Task<Plan?> PlanAsync(IUnitOfWork unit, Guid playerId, IReadOnlyDictionary<int, int> obtained, DateTimeOffset obtainedAt, bool isNew, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(obtained);

        if (obtained.Count == 0)
        {
            return null;
        }

        Dictionary<int, Tally> shelf = isNew ? [] : await _shelves.GetAsync(playerId, ct => this.LoadAsync(playerId, ct), cancellationToken).ConfigureAwait(false);
        Dictionary<int, Tally> next = new(shelf);
        IRepository<PlayerDiscovery> rows = unit.Repository<PlayerDiscovery>();
        foreach (KeyValuePair<int, int> item in obtained)
        {
            if (next.TryGetValue(item.Key, out Tally held))
            {
                // Written by key, with the count only: the row is not loaded in this unit, so there is nothing to conflict with.
                PlayerDiscovery change = new() { PlayerId = playerId, ItemId = item.Key, Count = held.Count };
                change.Obtain(item.Value);
                rows.Update(change, nameof(PlayerDiscovery.Count));
                next[item.Key] = new Tally(change.Count, held.FirstObtainedAt);
            }
            else
            {
                rows.Add(PlayerDiscovery.Start(playerId, item.Key, item.Value, obtainedAt));
                next[item.Key] = new Tally(item.Value, obtainedAt);
            }
        }

        return new Plan(playerId, next);
    }

    /// <summary>Keeps the journal of a plan whose save succeeded.</summary>
    /// <param name="plan">The plan that was saved.</param>
    public void Commit(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _shelves.Set(plan.PlayerId, plan.Next);
    }

    /// <summary>Drops a player's journal from memory; the next grant reads the rows again. Used when a save failed and when the player leaves.</summary>
    /// <param name="playerId">The player.</param>
    public void Forget(Guid playerId) => _shelves.Invalidate(playerId);

    // Reads the rows in a unit of its own: the grant's unit may already track its own rows, and this read must not depend on them.
    private async Task<Dictionary<int, Tally>> LoadAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<PlayerDiscovery> rows = await unit.Repository<PlayerDiscovery>().ListAsync(d => d.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            Dictionary<int, Tally> shelf = new(rows.Count);
            foreach (PlayerDiscovery row in rows)
            {
                shelf[row.ItemId] = new Tally(row.Count, row.FirstObtainedAt);
            }

            return shelf;
        }
    }

    /// <summary>The journal of one player as it will be once the save succeeds.</summary>
    /// <param name="PlayerId">The player.</param>
    /// <param name="Next">The discovered items, by item id.</param>
    internal sealed record Plan(Guid PlayerId, Dictionary<int, Tally> Next);

    /// <summary>What the journal keeps of one item.</summary>
    /// <param name="Count">How many of the item the player has obtained in total.</param>
    /// <param name="FirstObtainedAt">When the player first obtained the item.</param>
    internal readonly record struct Tally(int Count, DateTimeOffset FirstObtainedAt);
}
