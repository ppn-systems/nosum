// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Infrastructure.Concurrency;
using Nosum.Domain.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Inventory;

/// <summary>
/// <see cref="IInventoryService"/> on PostgreSQL. The stacking rules are <see cref="InventoryGrid"/>;
/// this class loads a player's slots into a grid, applies the rules, writes only the changed rows
/// and the audit entries in one transaction, and pushes the changed slots. Changes of one player are
/// serialized by a striped in-process lock (the server is a single process, docs/adr/ADR-009); the
/// primary key (player, slot) is the backstop that turns a lost race into
/// <see cref="InventoryStatus.Conflict"/>.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private const string StarterReason = "starter";

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;
    private readonly StripedAsyncLock _locks = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock for audit timestamps; tests pass a fake one.</param>
    /// <param name="notifier">Pushes changes to the player; <see langword="null"/> to push nothing.</param>
    public InventoryService(IUnitOfWorkFactory unitOfWork, TimeProvider time, IPlayerNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _time = time;
        _notifier = notifier;
    }

    /// <inheritdoc/>
    public async Task<InventoryView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        Outcome outcome = await this.ExecuteAsync(playerId, static (_, _) => InventoryStatus.Ok, string.Empty, null, push: false, cancellationToken).ConfigureAwait(false);
        return outcome.View;
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
            (loaded, deltas) => ApplyOperations(loaded.Grid, operations, deltas),
            reason,
            enlist,
            push: true,
            cancellationToken).ConfigureAwait(false);
        return new InventoryChange(outcome.Status, outcome.Changed);
    }

    /// <inheritdoc/>
    public Task<InventoryChange> AddAsync(Guid playerId, int itemId, int quantity, string reason, CancellationToken cancellationToken = default)
        => this.ApplyAsync(playerId, [InventoryOperation.Add(itemId, quantity)], reason, null, cancellationToken);

    /// <inheritdoc/>
    public Task<InventoryChange> RemoveAsync(Guid playerId, int itemId, int quantity, string reason, CancellationToken cancellationToken = default)
        => this.ApplyAsync(playerId, [InventoryOperation.Remove(itemId, quantity)], reason, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<InventoryChange> MoveAsync(Guid playerId, int fromSlot, int toSlot, int quantity, CancellationToken cancellationToken = default)
    {
        // A move changes no totals, so there is nothing to audit.
        Outcome outcome = await this.ExecuteAsync(
            playerId,
            (loaded, _) => ToStatus(loaded.Grid.Move(fromSlot, toSlot, quantity)),
            string.Empty,
            null,
            push: true,
            cancellationToken).ConfigureAwait(false);
        return new InventoryChange(outcome.Status, outcome.Changed);
    }

    /// <inheritdoc/>
    public async Task<(InventoryStatus Status, int SelectedSlot)> SelectSlotAsync(Guid playerId, int slot, CancellationToken cancellationToken = default)
    {
        Outcome outcome = await this.ExecuteAsync(
            playerId,
            (loaded, _) =>
            {
                if ((uint)slot >= (uint)InventoryLimits.HotbarSlots)
                {
                    return InventoryStatus.InvalidSlot;
                }

                loaded.Header.SelectedSlot = slot;
                return InventoryStatus.Ok;
            },
            string.Empty,
            null,
            push: false,
            cancellationToken).ConfigureAwait(false);
        return (outcome.Status, outcome.View.SelectedSlot);
    }

    /// <inheritdoc/>
    public async Task<InventorySlotView> PeekSlotAsync(Guid playerId, int slot, CancellationToken cancellationToken = default)
    {
        Outcome outcome = await this.ExecuteAsync(playerId, static (_, _) => InventoryStatus.Ok, string.Empty, null, push: false, cancellationToken).ConfigureAwait(false);
        foreach (InventorySlotView view in outcome.View.Slots)
        {
            if (view.Slot == slot)
            {
                return view;
            }
        }

        return new InventorySlotView(slot, 0, 0);
    }

    private static InventoryStatus ApplyOperations(InventoryGrid grid, IReadOnlyList<InventoryOperation> operations, List<(int ItemId, int Delta)> deltas)
    {
        foreach (InventoryOperation operation in operations)
        {
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

    private static InventoryView ViewOf(PlayerInventory header, InventoryGrid grid)
    {
        List<InventorySlotView> slots = new(grid.Capacity);
        for (int i = 0; i < grid.Capacity; i++)
        {
            ItemSlot stack = grid[i];
            if (!stack.IsEmpty)
            {
                slots.Add(new InventorySlotView(i, stack.ItemId, stack.Quantity));
            }
        }

        return new InventoryView(header.Capacity, header.SelectedSlot, slots);
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
                    InventoryStatus status = ApplyOperations(loaded[i].Grid, changes[i].Operations, deltas[i]);
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

    // Loads the players' inventories (creating them with the starter kit), runs one change under all their locks,
    // writes the changed rows plus the audit entries and the enlisted work in one save, then pushes.
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
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                Loaded[] loaded = new Loaded[playerIds.Length];
                List<(int ItemId, int Delta)>[] deltas = new List<(int ItemId, int Delta)>[playerIds.Length];
                for (int i = 0; i < playerIds.Length; i++)
                {
                    loaded[i] = await this.LoadAsync(unit, playerIds[i], cancellationToken).ConfigureAwait(false);
                    deltas[i] = [];
                }

                InventoryStatus status = change(loaded, deltas);
                if (status != InventoryStatus.Ok)
                {
                    // Nothing is saved, not even a starter kit: the next call creates it again.
                    return Refusal(status, loaded);
                }

                List<InventorySlotView>[] changed = new List<InventorySlotView>[playerIds.Length];
                for (int i = 0; i < playerIds.Length; i++)
                {
                    List<int> changedSlots = [];
                    loaded[i].Grid.TakeChanged(changedSlots);
                    changed[i] = this.WriteRows(unit, loaded[i], changedSlots, deltas[i], reason);
                }

                if (enlist is not null && !await enlist(unit, cancellationToken).ConfigureAwait(false))
                {
                    return Refusal(InventoryStatus.Conflict, loaded);
                }

                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
                {
                    return Refusal(InventoryStatus.Conflict, loaded);
                }

                List<InventoryView> views = new(loaded.Length);
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (push && changed[i].Count > 0)
                    {
                        await this.PushAsync(playerIds[i], changed[i], cancellationToken).ConfigureAwait(false);
                    }

                    views.Add(ViewOf(loaded[i].Header, loaded[i].Grid));
                }

                return new ManyOutcome(InventoryStatus.Ok, changed, views);
            }
        }
    }

    private static ManyOutcome Refusal(InventoryStatus status, Loaded[] loaded)
    {
        List<List<InventorySlotView>> none = [];
        List<InventoryView> views = [];
        foreach (Loaded one in loaded)
        {
            none.Add([]);
            views.Add(ViewOf(one.Header, one.Grid));
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
            Dictionary<int, InventorySlot> starterRows = [];
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
            foreach (int slot in starterSlots)
            {
                InventorySlot row = new() { PlayerId = playerId, Slot = slot, ItemId = fresh[slot].ItemId, Quantity = fresh[slot].Quantity };
                slotRows.Add(row);
                starterRows[slot] = row;
            }

            return new Loaded(header, fresh, starterRows);
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

        return new Loaded(header, grid, bySlot);
    }

    // Brings the slot rows in line with the grid for the changed slots and adds the audit entries.
    private List<InventorySlotView> WriteRows(IUnitOfWork unit, Loaded loaded, List<int> changedSlots, List<(int ItemId, int Delta)> deltas, string reason)
    {
        IRepository<InventorySlot> slots = unit.Repository<InventorySlot>();
        List<InventorySlotView> changed = new(changedSlots.Count);
        foreach (int slot in changedSlots)
        {
            ItemSlot stack = loaded.Grid[slot];
            _ = loaded.Rows.TryGetValue(slot, out InventorySlot? row);
            if (stack.IsEmpty)
            {
                if (row is not null)
                {
                    slots.Remove(row);
                }
            }
            else if (row is null)
            {
                slots.Add(new InventorySlot { PlayerId = loaded.Header.PlayerId, Slot = slot, ItemId = stack.ItemId, Quantity = stack.Quantity });
            }
            else
            {
                row.ItemId = stack.ItemId;
                row.Quantity = stack.Quantity;
            }

            changed.Add(new InventorySlotView(slot, stack.ItemId, stack.Quantity));
        }

        // One audit entry per item for the change itself. A container has no player to log against: its movements are logged by the feature that made them (chest_log).
        List<(int ItemId, int Delta, string Reason)> entries = [];
        if (!ContainerIds.IsContainer(loaded.Header.PlayerId))
        {
            AddMerged(entries, deltas, reason);
        }

        DateTimeOffset now = _time.GetUtcNow();
        IRepository<InventoryLogEntry> log = unit.Repository<InventoryLogEntry>();
        foreach ((int itemId, int delta, string why) in entries)
        {
            log.Add(new InventoryLogEntry { PlayerId = loaded.Header.PlayerId, ItemId = itemId, Delta = delta, Reason = why, OccurredAt = now });
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

    private sealed record Loaded(PlayerInventory Header, InventoryGrid Grid, Dictionary<int, InventorySlot> Rows);

    private sealed record Outcome(InventoryStatus Status, IReadOnlyList<InventorySlotView> Changed, InventoryView View);

    private sealed record ManyOutcome(InventoryStatus Status, IReadOnlyList<IReadOnlyList<InventorySlotView>> Changed, IReadOnlyList<InventoryView> Views);
}
