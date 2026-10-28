// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Inventory;
using Nosum.Contracts.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Inventory;

public sealed partial class InventoryService
{
    /// <inheritdoc/>
    public async Task<InventoryView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        if (await this.TryReadAsync(playerId, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            return entry.View();
        }

        Outcome outcome = await this.ExecuteAsync(playerId, static (_, _) => InventoryStatus.Ok, string.Empty, null, push: false, cancellationToken).ConfigureAwait(false);
        return outcome.View;
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
    public async Task<InventoryChange> BackfillStarterToolsAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        Outcome outcome = await this.ExecuteAsync(
            playerId,
            static (loaded, deltas) =>
            {
                foreach (ItemSlot starter in StarterKit.Items)
                {
                    // A tool is skipped when the player already holds any tier of that tool kind; a tool with no room is tried again on the next join.
                    if (ItemCatalog.TryGet(starter.ItemId, out ItemDefinition? item)
                        && item.Kind == ItemKind.Tool
                        && loaded.Grid.CountOf(item.Id) == 0
                        && (item.Tool == ToolKind.None || !HasToolOfKind(loaded.Grid, loaded.Capacity, item.Tool))
                        && loaded.Grid.Add(item.Id, 1) == InventoryVerdict.Ok)
                    {
                        deltas.Add((item.Id, 1));
                    }
                }

                return InventoryStatus.Ok;
            },
            StarterBackfillReason,
            null,
            push: true,
            cancellationToken).ConfigureAwait(false);
        return new InventoryChange(outcome.Status, outcome.Changed);
    }

    private static bool HasToolOfKind(InventoryGrid grid, int capacity, ToolKind kind)
    {
        for (int i = 0; i < capacity; i++)
        {
            ItemSlot slot = grid[i];
            if (!slot.IsEmpty && ItemCatalog.TryGet(slot.ItemId, out ItemDefinition? existing) && existing.Kind == ItemKind.Tool && existing.Tool == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<InventorySlotView> PeekSlotAsync(Guid playerId, int slot, CancellationToken cancellationToken = default)
    {
        if (await this.TryReadAsync(playerId, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            return entry.Peek(slot);
        }

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

    /// <summary>Describes the copy held for an owner without loading it, for the audit of the caches (a service with nothing held answers <see langword="null"/>).</summary>
    /// <param name="ownerId">The player or container.</param>
    /// <returns>The copy as text, or <see langword="null"/> when none is held.</returns>
    public string? DescribeHeld(Guid ownerId) => _cache.TryGetValue(ownerId, out Entry? entry) ? Describe(entry.View()) : null;

    /// <summary>Describes an inventory as a service with nothing held reads it from the rows.</summary>
    /// <param name="view">The inventory.</param>
    /// <returns>The inventory as text.</returns>
    public static string Describe(InventoryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return $"capacity={view.Capacity};selected={view.SelectedSlot};" + string.Join(',', view.Slots.Select(s => $"{s.Slot}:{s.ItemId}x{s.Quantity}"));
    }

    /// <inheritdoc/>
    public void Forget(Guid ownerId) => _ = _cache.TryRemove(ownerId, out _);

    private void Forget(Guid[] ownerIds)
    {
        foreach (Guid id in ownerIds)
        {
            this.Forget(id);
        }
    }

    // A read of the copy queues behind a change that holds the player's lock, so a read that starts after a change has begun sees its result, as it did when every read hit the rows.
    private async ValueTask<Entry?> TryReadAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (!_cache.ContainsKey(ownerId))
        {
            return null;
        }

        using (await _locks.EnterAllAsync([ownerId], cancellationToken).ConfigureAwait(false))
        {
            return this.TryGetEntry(ownerId, out Entry? entry) ? entry : null;
        }
    }

    private bool TryGetEntry(Guid ownerId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entry? entry)
    {
        if (_cache.TryGetValue(ownerId, out entry))
        {
            entry.Touch();
            return true;
        }

        return false;
    }

    private void Store(Guid ownerId, Loaded loaded)
    {
        ItemSlot[] slots = new ItemSlot[loaded.Capacity];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = loaded.Grid[i];
        }

        _cache[ownerId] = new Entry(loaded.Capacity, loaded.SelectedSlot, loaded.CreatedAt, slots);
    }

    // Players leave with their session (Forget) and with this sweep when the sign-out was missed; containers are also capped, least recently used first.
    private void TrimIdle()
    {
        if ((Interlocked.Increment(ref _operations) & 0xFF) != 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        int containers = 0;
        foreach (KeyValuePair<Guid, Entry> pair in _cache)
        {
            if (now - pair.Value.LastUsed > IdleLimitMilliseconds)
            {
                _ = _cache.TryRemove(pair.Key, out _);
            }
            else if (ContainerIds.IsContainer(pair.Key))
            {
                containers++;
            }
        }

        if (containers > MaxContainers)
        {
            foreach (Guid id in _cache.Where(p => ContainerIds.IsContainer(p.Key)).OrderBy(p => p.Value.LastUsed).Take(containers - MaxContainers).Select(p => p.Key).ToList())
            {
                _ = _cache.TryRemove(id, out _);
            }
        }
    }

    private const long IdleLimitMilliseconds = 30 * 60 * 1000;
    private const int MaxContainers = 256;

    // An owner's inventory as last saved. Immutable once stored; a change builds a new one.
    private sealed class Entry
    {
        private long _lastUsed = Environment.TickCount64;

        public Entry(int capacity, int selectedSlot, DateTimeOffset createdAt, ItemSlot[] slots)
        {
            this.Capacity = capacity;
            this.SelectedSlot = selectedSlot;
            this.CreatedAt = createdAt;
            this.Slots = slots;
        }

        public int Capacity { get; }

        public int SelectedSlot { get; }

        public DateTimeOffset CreatedAt { get; }

        public ItemSlot[] Slots { get; }

        public long LastUsed => Volatile.Read(ref _lastUsed);

        public Entry WithSelectedSlot(int slot) => new(this.Capacity, slot, this.CreatedAt, this.Slots);

        public bool HoldsItems => this.Slots.Any(s => !s.IsEmpty);

        public void Touch() => Volatile.Write(ref _lastUsed, Environment.TickCount64);

        public InventorySlotView Peek(int slot)
            => (uint)slot < (uint)this.Slots.Length && !this.Slots[slot].IsEmpty ? new InventorySlotView(slot, this.Slots[slot].ItemId, this.Slots[slot].Quantity) : new InventorySlotView(slot, 0, 0);

        public InventoryView View()
        {
            List<InventorySlotView> slots = new(this.Slots.Length);
            for (int i = 0; i < this.Slots.Length; i++)
            {
                if (!this.Slots[i].IsEmpty)
                {
                    slots.Add(new InventorySlotView(i, this.Slots[i].ItemId, this.Slots[i].Quantity));
                }
            }

            return new InventoryView(this.Capacity, this.SelectedSlot, slots);
        }
    }

    private sealed record Outcome(InventoryStatus Status, IReadOnlyList<InventorySlotView> Changed, InventoryView View);

    private sealed record ManyOutcome(InventoryStatus Status, IReadOnlyList<IReadOnlyList<InventorySlotView>> Changed, IReadOnlyList<InventoryView> Views);
}
