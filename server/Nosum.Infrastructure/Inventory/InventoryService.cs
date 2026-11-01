// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Infrastructure.Caching;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Journal;
using Nosum.Domain.Inventory;
using Nosum.Domain.Journal;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Inventory;

/// <summary>
/// <see cref="IInventoryService"/> on PostgreSQL. The stacking rules are <see cref="InventoryGrid"/>;
/// this class loads a player's slots into a grid, applies the rules, writes only the changed rows
/// and the audit entries in one transaction, and pushes the changed slots. Changes of one player are
/// serialized by a striped in-process lock (the server is a single process, docs/adr/ADR-009); the
/// primary key (player, slot) is the backstop that turns a lost race into
/// <see cref="InventoryStatus.Conflict"/>.
/// <para>
/// Docs/adr/ADR-035 D3: the slot grid of every owner that was touched is kept in memory, so a read costs no command and a change costs only its writes.
/// This class is the only writer of the inventory tables, and it changes a cached entry only after the save succeeded, under the same locks; any failure drops the entries of
/// all parties, so the next call reloads them from the rows. The database stays the record: every change is written in the transaction that made it.
/// </para>
/// </summary>
public sealed partial class InventoryService : IInventoryService
{
    private const string StarterReason = "starter";
    private const string StarterBackfillReason = "starter.backfill";
    private const string SelectedSlotKey = "inventory.selected-slot";

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;
    private readonly StripedAsyncLock _locks = new();
    private readonly WriteBehind? _writeBehind;
    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();
    private readonly JournalRecorder _journal;
    private int _operations;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock for audit timestamps; tests pass a fake one.</param>
    /// <param name="notifier">Pushes changes to the player; <see langword="null"/> to push nothing.</param>
    /// <param name="writeBehind">Where the selected hotbar slot waits for its write (docs/adr/ADR-035 D7); <see langword="null"/> writes it at once.</param>
    public InventoryService(IUnitOfWorkFactory unitOfWork, TimeProvider time, IPlayerNotifier? notifier = null, WriteBehind? writeBehind = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _time = time;
        _notifier = notifier;
        _writeBehind = writeBehind;
        _journal = new JournalRecorder(unitOfWork);
    }

    /// <inheritdoc/>
    public async Task<InventoryChange> ApplyAsync(
        Guid playerId,
        IReadOnlyList<InventoryOperation> operations,
        string reason,
        InventoryEnlistment? enlist = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        Outcome outcome = await this.ExecuteAsync(
            playerId,
            (loaded, deltas) => ApplyOperations(loaded, operations, deltas),
            reason,
            enlist,
            push: true,
            cancellationToken).ConfigureAwait(false);
        return new InventoryChange(outcome.Status, outcome.Changed);
    }

    /// <inheritdoc/>
    public async Task<(InventoryStatus Status, int SelectedSlot)> SelectSlotAsync(Guid playerId, int slot, CancellationToken cancellationToken = default)
    {
        if (_writeBehind is not null && (uint)slot < (uint)InventoryLimits.HotbarSlots && !ContainerIds.IsContainer(playerId))
        {
            // Loaded (and created for a new player) first, then the choice is kept in memory and its row is written within the window, or when the player leaves.
            _ = await this.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            using (await _locks.EnterAllAsync([playerId], cancellationToken).ConfigureAwait(false))
            {
                if (_cache.TryGetValue(playerId, out Entry? entry))
                {
                    if (entry.SelectedSlot != slot)
                    {
                        _cache[playerId] = entry.WithSelectedSlot(slot);
                        _writeBehind.Stage(playerId, SelectedSlotKey, slot, WriteBehind.DefaultWindow, (unit, chosen) => unit.Repository<PlayerInventory>().Update(new PlayerInventory { PlayerId = playerId, SelectedSlot = chosen }, nameof(PlayerInventory.SelectedSlot)));
                    }

                    return (InventoryStatus.Ok, slot);
                }
            }
        }

        Outcome outcome = await this.ExecuteAsync(
            playerId,
            (loaded, _) =>
            {
                if ((uint)slot >= (uint)InventoryLimits.HotbarSlots)
                {
                    return InventoryStatus.InvalidSlot;
                }

                loaded.SelectedSlot = slot;
                return InventoryStatus.Ok;
            },
            string.Empty,
            null,
            push: false,
            cancellationToken).ConfigureAwait(false);
        return (outcome.Status, outcome.View.SelectedSlot);
    }

    private static InventoryStatus ApplyOperations(Loaded loaded, IReadOnlyList<InventoryOperation> operations, List<(int ItemId, int Delta)> deltas)
    {
        foreach (InventoryOperation operation in operations)
        {
            if (operation.Kind == InventoryOperationKind.Grow)
            {
                // A growth moves no items, so it adds no deltas and no audit lines; a refused growth refuses the whole change.
                InventoryStatus grown = loaded.Grow(operation.Quantity);
                if (grown != InventoryStatus.Ok)
                {
                    return grown;
                }

                continue;
            }

            InventoryGrid grid = loaded.Grid;
            InventoryVerdict verdict = operation.Kind switch
            {
                InventoryOperationKind.Add => grid.Add(operation.ItemId, operation.Quantity),
                InventoryOperationKind.Remove => grid.Remove(operation.ItemId, operation.Quantity),
                InventoryOperationKind.RemoveFromSlot => grid.RemoveFromSlot(operation.Slot, operation.ItemId, operation.Quantity),
                InventoryOperationKind.AddToSlot => grid.AddToSlot(operation.Slot, operation.ItemId, operation.Quantity),
                _ => InventoryVerdict.InvalidQuantity,
            };
            if (verdict != InventoryVerdict.Ok)
            {
                return ToStatus(verdict);
            }

            deltas.Add((operation.ItemId, operation.Kind is InventoryOperationKind.Add or InventoryOperationKind.AddToSlot ? operation.Quantity : -operation.Quantity));
        }

        return InventoryStatus.Ok;
    }

    private static InventoryStatus ToStatus(InventoryVerdict verdict) => verdict switch
    {
        InventoryVerdict.Ok => InventoryStatus.Ok,
        InventoryVerdict.InvalidSlot => InventoryStatus.InvalidSlot,
        InventoryVerdict.EmptySlot => InventoryStatus.EmptySlot,
        InventoryVerdict.InvalidQuantity => InventoryStatus.InvalidQuantity,
        InventoryVerdict.UnknownItem => InventoryStatus.UnknownItem,
        InventoryVerdict.NotEnough => InventoryStatus.NotEnough,
        InventoryVerdict.NoSpace => InventoryStatus.NoSpace,
        _ => InventoryStatus.Blocked,
    };

    private static InventoryView ViewOf(Loaded loaded)
    {
        InventoryGrid grid = loaded.Grid;
        List<InventorySlotView> slots = new(grid.Capacity);
        for (int i = 0; i < grid.Capacity; i++)
        {
            ItemSlot stack = grid[i];
            if (!stack.IsEmpty)
            {
                slots.Add(new InventorySlotView(i, stack.ItemId, stack.Quantity));
            }
        }

        return new InventoryView(loaded.Capacity, loaded.SelectedSlot, slots);
    }

    /// <inheritdoc/>
    public async Task<InventoryStatus> ApplyManyAsync(
        IReadOnlyList<PlayerInventoryChange> changes,
        string reason,
        InventoryEnlistment? enlist = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        if (changes.Count == 0)
        {
            return InventoryStatus.Ok;
        }

        Guid[] players = new Guid[changes.Count];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = changes[i].PlayerId;
        }

        if (new HashSet<Guid>(players).Count != players.Length)
        {
            throw new ArgumentException("A player may appear once in a combined change.", nameof(changes));
        }

        ManyOutcome outcome = await this.ExecuteManyAsync(
            players,
            (loaded, deltas) =>
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    InventoryStatus status = ApplyOperations(loaded[i], changes[i].Operations, deltas[i]);
                    if (status != InventoryStatus.Ok)
                    {
                        return status;
                    }
                }

                return InventoryStatus.Ok;
            },
            reason,
            enlist,
            push: true,
            cancellationToken).ConfigureAwait(false);
        return outcome.Status;
    }

    // One player: the same machinery as several.
    private async Task<Outcome> ExecuteAsync(
        Guid playerId,
        Func<Loaded, List<(int ItemId, int Delta)>, InventoryStatus> change,
        string reason,
        InventoryEnlistment? enlist,
        bool push,
        CancellationToken cancellationToken)
    {
        ManyOutcome many = await this.ExecuteManyAsync(
            [playerId],
            (loaded, deltas) => change(loaded[0], deltas[0]),
            reason,
            enlist,
            push,
            cancellationToken).ConfigureAwait(false);
        return new Outcome(many.Status, many.Changed[0], many.Views[0]);
    }

    // Takes the copy of each player's inventory (from memory, else from the rows, creating it with the starter kit), runs one change under all their locks,
    // writes the changed rows plus the audit entries and the enlisted work in one save, moves the cache, then pushes.
    private async Task<ManyOutcome> ExecuteManyAsync(
        Guid[] playerIds,
        Func<Loaded[], List<(int ItemId, int Delta)>[], InventoryStatus> change,
        string reason,
        InventoryEnlistment? enlist,
        bool push,
        CancellationToken cancellationToken)
    {
        using (await _locks.EnterAllAsync(playerIds, cancellationToken).ConfigureAwait(false))
        {
            this.TrimIdle();
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                try
                {
                    return await this.RunAsync(unit, playerIds, change, reason, enlist, push, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Whatever failed, the rows and the copies may disagree: the next call reads the rows again (docs/adr/ADR-035 D3 rule 2).
                    this.Forget(playerIds);
                    throw;
                }
            }
        }
    }

    private async Task<ManyOutcome> RunAsync(
        IUnitOfWork unit,
        Guid[] playerIds,
        Func<Loaded[], List<(int ItemId, int Delta)>[], InventoryStatus> change,
        string reason,
        InventoryEnlistment? enlist,
        bool push,
        CancellationToken cancellationToken)
    {
        Loaded[] loaded = new Loaded[playerIds.Length];
        List<(int ItemId, int Delta)>[] deltas = new List<(int ItemId, int Delta)>[playerIds.Length];
        for (int i = 0; i < playerIds.Length; i++)
        {
            loaded[i] = this.TryGetEntry(playerIds[i], out Entry? entry)
                ? Loaded.FromEntry(playerIds[i], entry)
                : await this.LoadAsync(unit, playerIds[i], cancellationToken).ConfigureAwait(false);
            deltas[i] = [];
        }

        InventoryStatus status = change(loaded, deltas);
        if (status != InventoryStatus.Ok)
        {
            // Nothing is saved, not even a starter kit: the next call creates it again. The copies are untouched, the working grids are thrown away.
            return Refusal(status, loaded);
        }

        List<InventorySlotView>[] changed = new List<InventorySlotView>[playerIds.Length];
        bool wrote = false;
        for (int i = 0; i < playerIds.Length; i++)
        {
            List<int> changedSlots = loaded[i].TakeChangedSlots();
            changed[i] = this.WriteRows(unit, loaded[i], changedSlots, deltas[i], reason, ref wrote);
        }

        // The collection journal: the items this change obtained are discovered in the same transaction, so they are saved if and only if the items are.
        JournalRecorder.Plan?[] journal = new JournalRecorder.Plan?[playerIds.Length];
        for (int i = 0; i < playerIds.Length; i++)
        {
            journal[i] = await this.PlanJournalAsync(unit, loaded[i], deltas[i], cancellationToken).ConfigureAwait(false);
            wrote |= journal[i] is not null;
        }

        if (enlist is not null && !await enlist(unit, cancellationToken).ConfigureAwait(false))
        {
            this.Forget(playerIds);
            return Refusal(InventoryStatus.Conflict, loaded);
        }

        if (wrote || enlist is not null)
        {
            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                this.Forget(playerIds);
                return Refusal(InventoryStatus.Conflict, loaded);
            }
        }

        // Saved: only now do the copies move, still under every party's lock.
        List<InventoryView> views = new(loaded.Length);
        for (int i = 0; i < loaded.Length; i++)
        {
            this.Store(playerIds[i], loaded[i]);
            if (journal[i] is { } saved)
            {
                _journal.Commit(saved);
            }

            if (push && changed[i].Count > 0)
            {
                await this.PushAsync(playerIds[i], changed[i], cancellationToken).ConfigureAwait(false);
            }

            views.Add(ViewOf(loaded[i]));
        }

        return new ManyOutcome(InventoryStatus.Ok, changed, views);
    }

    private static ManyOutcome Refusal(InventoryStatus status, Loaded[] loaded)
    {
        List<List<InventorySlotView>> none = [];
        List<InventoryView> views = [];
        foreach (Loaded one in loaded)
        {
            none.Add([]);
            views.Add(ViewOf(one));
        }

        return new ManyOutcome(status, none, views);
    }

    private async Task<Loaded> LoadAsync(IUnitOfWork unit, Guid playerId, CancellationToken cancellationToken)
    {
        IRepository<PlayerInventory> headers = unit.Repository<PlayerInventory>();
        PlayerInventory? header = await headers.FirstOrDefaultAsync(i => i.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            // A container (a chest) starts empty and has its own size; only a player gets the starter kit.
            bool container = ContainerIds.IsContainer(playerId);
            header = new PlayerInventory { PlayerId = playerId, Capacity = container ? ChestRules.Capacity : InventoryLimits.DefaultCapacity, SelectedSlot = 0, CreatedAt = _time.GetUtcNow() };
            headers.Add(header);
            InventoryGrid fresh = new(header.Capacity);
            IRepository<InventorySlot> slotRows = unit.Repository<InventorySlot>();
            IRepository<InventoryLogEntry> log = unit.Repository<InventoryLogEntry>();
            foreach (ItemSlot item in container ? [] : StarterKit.Items)
            {
                if (fresh.Add(item.ItemId, item.Quantity) == InventoryVerdict.Ok)
                {
                    log.Add(new InventoryLogEntry { PlayerId = playerId, ItemId = item.ItemId, Delta = item.Quantity, Reason = StarterReason, OccurredAt = header.CreatedAt });
                }
            }

            // The starter kit is written with the header; it is not a change of the call that happened to create it.
            List<int> starterSlots = [];
            fresh.TakeChanged(starterSlots);
            Dictionary<int, InventorySlot> starterRows = [];
            foreach (int slot in starterSlots)
            {
                InventorySlot row = new() { PlayerId = playerId, Slot = slot, ItemId = fresh[slot].ItemId, Quantity = fresh[slot].Quantity };
                slotRows.Add(row);
                starterRows[slot] = row;
            }

            return new Loaded(playerId, header.Capacity, header.SelectedSlot, header.CreatedAt, fresh, header, starterRows, isNew: true, before: null);
        }

        List<InventorySlot> rows = await unit.Repository<InventorySlot>().ListAsync(s => s.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        InventoryGrid grid = new(header.Capacity);
        Dictionary<int, InventorySlot> bySlot = new(rows.Count);
        foreach (InventorySlot row in rows)
        {
            if (row.Slot >= 0 && row.Slot < grid.Capacity)
            {
                grid.Load(row.Slot, new ItemSlot(row.ItemId, row.Quantity));
                bySlot[row.Slot] = row;
            }
        }

        // A choice that is still waiting for its write is the truth (the row is older).
        int selected = _writeBehind is not null && _writeBehind.TryPeek(playerId, SelectedSlotKey, out int waiting) ? waiting : header.SelectedSlot;
        return new Loaded(playerId, header.Capacity, selected, header.CreatedAt, grid, header, bySlot, isNew: false, before: null);
    }

    // Plans what one player obtained in this change into their collection journal: the positive changes, plus the starter kit when this change created the inventory.
    // Moves and sorts pass no changes, so they obtain nothing; containers have no journal. The plan is kept only after the save (see ExecuteManyAsync).
    private async Task<JournalRecorder.Plan?> PlanJournalAsync(IUnitOfWork unit, Loaded loaded, List<(int ItemId, int Delta)> deltas, CancellationToken cancellationToken)
    {
        if (ContainerIds.IsContainer(loaded.PlayerId))
        {
            return null;
        }

        List<(int ItemId, int Delta)> gained = [.. deltas];
        if (loaded.IsNew)
        {
            foreach (ItemSlot item in StarterKit.Items)
            {
                gained.Add((item.ItemId, item.Quantity));
            }
        }

        return await _journal.PlanAsync(unit, loaded.PlayerId, JournalRules.ObtainedTotals(gained), _time.GetUtcNow(), loaded.IsNew, cancellationToken).ConfigureAwait(false);
    }

    // Brings the slot rows in line with the grid for the changed slots and adds the audit entries.
    private List<InventorySlotView> WriteRows(IUnitOfWork unit, Loaded loaded, List<int> changedSlots, List<(int ItemId, int Delta)> deltas, string reason, ref bool wrote)
    {
        IRepository<InventorySlot> slots = unit.Repository<InventorySlot>();
        List<InventorySlotView> changed = new(changedSlots.Count);
        foreach (int slot in changedSlots)
        {
            ItemSlot stack = loaded.Grid[slot];
            if (loaded.Before is { } before)
            {
                // From memory: no entity was loaded, so the rows are written by key. A row exists exactly when the slot held something.
                // A slot past the size the copy had (the bag grew in this change) has no row yet.
                bool existed = slot < before.Length && !before[slot].IsEmpty;
                if (stack.IsEmpty)
                {
                    if (existed)
                    {
                        slots.Remove(new InventorySlot { PlayerId = loaded.PlayerId, Slot = slot });
                        wrote = true;
                    }
                }
                else if (existed)
                {
                    slots.Update(new InventorySlot { PlayerId = loaded.PlayerId, Slot = slot, ItemId = stack.ItemId, Quantity = stack.Quantity });
                    wrote = true;
                }
                else
                {
                    slots.Add(new InventorySlot { PlayerId = loaded.PlayerId, Slot = slot, ItemId = stack.ItemId, Quantity = stack.Quantity });
                    wrote = true;
                }
            }
            else
            {
                _ = loaded.Rows!.TryGetValue(slot, out InventorySlot? row);
                if (stack.IsEmpty)
                {
                    if (row is not null)
                    {
                        slots.Remove(row);
                    }
                }
                else if (row is null)
                {
                    slots.Add(new InventorySlot { PlayerId = loaded.PlayerId, Slot = slot, ItemId = stack.ItemId, Quantity = stack.Quantity });
                }
                else
                {
                    row.ItemId = stack.ItemId;
                    row.Quantity = stack.Quantity;
                }

                wrote = true;
            }

            changed.Add(new InventorySlotView(slot, stack.ItemId, stack.Quantity));
        }

        // The header holds the size and the held slot; a growth is saved with the header in the same transaction as the slots it adds.
        if (loaded.Header is { } header)
        {
            header.Capacity = loaded.Capacity;
            header.SelectedSlot = loaded.SelectedSlot;
            wrote |= loaded.IsNew || loaded.SelectedSlot != loaded.OriginalSelectedSlot || loaded.Capacity != loaded.OriginalCapacity;
        }
        else if (loaded.SelectedSlot != loaded.OriginalSelectedSlot || loaded.Capacity != loaded.OriginalCapacity)
        {
            unit.Repository<PlayerInventory>().Update(new PlayerInventory { PlayerId = loaded.PlayerId, Capacity = loaded.Capacity, SelectedSlot = loaded.SelectedSlot, CreatedAt = loaded.CreatedAt });
            wrote = true;
        }

        // One audit entry per item for the change itself. A container has no player to log against: its movements are logged by the feature that made them (chest_log).
        List<(int ItemId, int Delta, string Reason)> entries = [];
        if (!ContainerIds.IsContainer(loaded.PlayerId))
        {
            AddMerged(entries, deltas, reason);
        }

        DateTimeOffset now = _time.GetUtcNow();
        IRepository<InventoryLogEntry> log = unit.Repository<InventoryLogEntry>();
        foreach ((int itemId, int delta, string why) in entries)
        {
            log.Add(new InventoryLogEntry { PlayerId = loaded.PlayerId, ItemId = itemId, Delta = delta, Reason = why, OccurredAt = now });
            wrote = true;
        }

        return changed;
    }

    private static void AddMerged(List<(int ItemId, int Delta, string Reason)> entries, List<(int ItemId, int Delta)> deltas, string reason)
    {
        Dictionary<int, int> totals = [];
        foreach ((int itemId, int delta) in deltas)
        {
            totals[itemId] = totals.GetValueOrDefault(itemId) + delta;
        }

        foreach ((int itemId, int delta) in totals)
        {
            if (delta != 0)
            {
                entries.Add((itemId, delta, reason));
            }
        }
    }

    private async Task PushAsync(Guid playerId, IReadOnlyList<InventorySlotView> changed, CancellationToken cancellationToken)
    {
        if (_notifier is null || ContainerIds.IsContainer(playerId))
        {
            return;
        }

        using PacketScope<InventoryUpdate> lease = PacketFactory<InventoryUpdate>.Acquire();
        lease.Value.Slots.Clear();
        foreach (InventorySlotView view in changed)
        {
            InventorySlotRow row = InventorySlotRow.Create();
            row.Slot = (byte)view.Slot;
            row.ItemId = view.ItemId;
            row.Quantity = view.Quantity;
            lease.Value.Slots.Add(row);
        }

        _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> DropContainerAsync(IUnitOfWork unit, Guid containerId, bool onlyIfEmpty, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        IRepository<PlayerInventory> headers = unit.Repository<PlayerInventory>();
        if (this.TryGetEntry(containerId, out Entry? entry))
        {
            if (onlyIfEmpty && entry.HoldsItems)
            {
                return false;
            }

            headers.Remove(new PlayerInventory { PlayerId = containerId });
        }
        else
        {
            if (onlyIfEmpty && await unit.Repository<InventorySlot>().AnyAsync(s => s.PlayerId == containerId, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            // A container that was never opened has no header; the empty chest's inventory goes with it otherwise.
            if (await headers.FindAsync(containerId, cancellationToken).ConfigureAwait(false) is { } header)
            {
                headers.Remove(header);
            }
        }

        // Dropped before the caller saves: a reader that reloads in between sees the rows, which are gone with the caller's save and cannot be reached any more (the chest is gone).
        this.Forget(containerId);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> AnyContainerHoldsItemsAsync(IUnitOfWork unit, IReadOnlyCollection<Guid> containerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(containerIds);

        List<Guid> unknown = [];
        foreach (Guid id in containerIds)
        {
            if (!this.TryGetEntry(id, out Entry? entry))
            {
                unknown.Add(id);
            }
            else if (entry.HoldsItems)
            {
                return true;
            }
        }

        return unknown.Count > 0 && await unit.Repository<InventorySlot>().AnyAsync(s => unknown.Contains(s.PlayerId), cancellationToken).ConfigureAwait(false);
    }

    // The working copy of one inventory for one change: a fresh grid, so a refusal half way through leaves the cached entry as it was.
    private sealed class Loaded
    {
        // Slots changed earlier in this change, before a growth replaced the grid; they are written with the rest.
        private readonly List<int> _carriedChanges = [];

        public Loaded(Guid playerId, int capacity, int selectedSlot, DateTimeOffset createdAt, InventoryGrid grid, PlayerInventory? header, Dictionary<int, InventorySlot>? rows, bool isNew, ItemSlot[]? before)
        {
            this.PlayerId = playerId;
            this.Capacity = capacity;
            this.OriginalCapacity = capacity;
            this.SelectedSlot = selectedSlot;
            this.OriginalSelectedSlot = selectedSlot;
            this.CreatedAt = createdAt;
            this.Grid = grid;
            this.Header = header;
            this.Rows = rows;
            this.IsNew = isNew;
            this.Before = before;
        }

        public Guid PlayerId { get; }

        public int Capacity { get; private set; }

        public int OriginalCapacity { get; }

        public int SelectedSlot { get; set; }

        public int OriginalSelectedSlot { get; }

        public DateTimeOffset CreatedAt { get; }

        public InventoryGrid Grid { get; private set; }

        // Set when the rows were loaded (tracked) or are new; null when the working copy came from memory.
        public PlayerInventory? Header { get; }

        public Dictionary<int, InventorySlot>? Rows { get; }

        public bool IsNew { get; }

        // The slots as the cache held them, when the working copy came from memory.
        public ItemSlot[]? Before { get; }

        // Grows the working copy to a larger bag (docs/adr/ADR-081): the stacks stay in their slots, nothing is logged, and a refusal changes nothing.
        public InventoryStatus Grow(int capacity)
        {
            if (ContainerIds.IsContainer(this.PlayerId) || !BackpackRules.MayGrow(this.Capacity, capacity))
            {
                return InventoryStatus.Conflict;
            }

            // A new grid starts with no changed slots: the ones changed so far are kept for the write.
            this.Grid.TakeChanged(_carriedChanges);
            InventoryGrid grown = new(capacity);
            for (int i = 0; i < this.Grid.Capacity; i++)
            {
                ItemSlot stack = this.Grid[i];
                if (!stack.IsEmpty)
                {
                    grown.Load(i, stack);
                }
            }

            this.Grid = grown;
            this.Capacity = capacity;
            return InventoryStatus.Ok;
        }

        // The slots to write, ascending and without repeats: those changed before a growth and those changed since.
        public List<int> TakeChangedSlots()
        {
            List<int> slots = [.. _carriedChanges];
            _carriedChanges.Clear();
            this.Grid.TakeChanged(slots);
            return slots.Distinct().Order().ToList();
        }

        public static Loaded FromEntry(Guid playerId, Entry entry)
        {
            InventoryGrid grid = new(entry.Capacity);
            for (int i = 0; i < entry.Slots.Length; i++)
            {
                if (!entry.Slots[i].IsEmpty)
                {
                    grid.Load(i, entry.Slots[i]);
                }
            }

            return new Loaded(playerId, entry.Capacity, entry.SelectedSlot, entry.CreatedAt, grid, null, null, isNew: false, before: entry.Slots);
        }
    }
}
