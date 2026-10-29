// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Inventory;

/// <summary>One slot's content. <see cref="ItemId"/> zero means the slot is empty.</summary>
/// <param name="ItemId">Item id, or zero for an empty slot.</param>
/// <param name="Quantity">How many are stacked; zero for an empty slot.</param>
public readonly record struct ItemSlot(int ItemId, int Quantity)
{
    /// <summary>Gets whether the slot holds nothing.</summary>
    public bool IsEmpty => this.ItemId == 0;
}

/// <summary>
/// The pure rules of an inventory: a fixed number of slots holding stacks. Stacking, adding,
/// removing and moving are all-or-nothing: a refused call changes nothing. The grid remembers which
/// slots changed so a caller can persist and send only those. No I/O, so server and client
/// (prediction) can run the same code and tests need no database.
/// </summary>
public sealed class InventoryGrid
{
    private readonly ItemSlot[] _slots;
    private readonly bool[] _changed;

    /// <summary>Initializes an empty inventory.</summary>
    /// <param name="capacity">Number of slots.</param>
    public InventoryGrid(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, InventoryLimits.MaxCapacity);
        _slots = new ItemSlot[capacity];
        _changed = new bool[capacity];
    }

    /// <summary>Gets the number of slots.</summary>
    public int Capacity => _slots.Length;

    /// <summary>Gets a slot's content.</summary>
    /// <param name="slot">Slot number, zero based.</param>
    /// <returns>The stack, or an empty stack for a slot outside the inventory.</returns>
    public ItemSlot this[int slot] => (uint)slot < (uint)_slots.Length ? _slots[slot] : default;

    /// <summary>Puts a stack into a slot as loaded from storage; not recorded as a change.</summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="stack">The stack; an unknown item or non-positive quantity is rejected.</param>
    public void Load(int slot, ItemSlot stack)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, _slots.Length);
        if (!ItemCatalog.TryGet(stack.ItemId, out ItemDefinition? item) || stack.Quantity <= 0 || stack.Quantity > item.MaxStack)
        {
            throw new ArgumentException("The stack is not a valid item stack.", nameof(stack));
        }

        _slots[slot] = stack;
    }

    /// <summary>Counts the items of one kind over all slots.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The total quantity.</returns>
    public int CountOf(int itemId)
    {
        int total = 0;
        foreach (ItemSlot stack in _slots)
        {
            if (stack.ItemId == itemId)
            {
                total += stack.Quantity;
            }
        }

        return total;
    }

    /// <summary>Tells whether <see cref="Add"/> would succeed.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many.</param>
    /// <returns><see cref="InventoryVerdict.Ok"/> or the reason it would be refused.</returns>
    public InventoryVerdict CanAdd(int itemId, int quantity)
    {
        if (quantity <= 0)
        {
            return InventoryVerdict.InvalidQuantity;
        }

        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return InventoryVerdict.UnknownItem;
        }

        long room = 0;
        foreach (ItemSlot stack in _slots)
        {
            room += stack.IsEmpty ? item.MaxStack : stack.ItemId == itemId ? item.MaxStack - stack.Quantity : 0;
            if (room >= quantity)
            {
                return InventoryVerdict.Ok;
            }
        }

        return InventoryVerdict.NoSpace;
    }

    /// <summary>Returns the total quantity of <paramref name="itemId"/> that can currently fit into this grid across existing stacks and empty slots.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The available room, or zero when the item is unknown or the grid is full.</returns>
    public int AvailableRoomFor(int itemId)
    {
        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return 0;
        }

        long room = 0;
        foreach (ItemSlot stack in _slots)
        {
            room += stack.IsEmpty ? item.MaxStack : stack.ItemId == itemId ? item.MaxStack - stack.Quantity : 0;
            if (room >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)room;
    }

    /// <summary>Adds as many of <paramref name="quantity"/> items as fit into the grid and returns how many were added.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">Desired quantity to add.</param>
    /// <returns>The quantity actually added (between 0 and <paramref name="quantity"/>).</returns>
    public int AddUpToCapacity(int itemId, int quantity)
    {
        if (quantity <= 0 || !ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return 0;
        }

        int fitting = Math.Min(quantity, this.AvailableRoomFor(itemId));
        return fitting <= 0 ? 0 : this.FillIntoSlots(itemId, item.MaxStack, fitting);
    }

    /// <summary>Adds items: tops up existing stacks first (lowest slot first), then fills empty slots.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many; all of them are added or none.</param>
    /// <returns><see cref="InventoryVerdict.Ok"/> or the reason nothing was added.</returns>
    public InventoryVerdict Add(int itemId, int quantity)
    {
        InventoryVerdict verdict = this.CanAdd(itemId, quantity);
        if (verdict != InventoryVerdict.Ok)
        {
            return verdict;
        }

        _ = ItemCatalog.TryGet(itemId, out ItemDefinition? item);
        _ = this.FillIntoSlots(itemId, item!.MaxStack, quantity);
        return InventoryVerdict.Ok;
    }

    private int FillIntoSlots(int itemId, int maxStack, int quantity)
    {
        int left = quantity;
        for (int i = 0; i < _slots.Length && left > 0; i++)
        {
            if (_slots[i].ItemId == itemId)
            {
                left -= this.Fill(i, itemId, maxStack, left);
            }
        }

        for (int i = 0; i < _slots.Length && left > 0; i++)
        {
            if (_slots[i].IsEmpty)
            {
                left -= this.Fill(i, itemId, maxStack, left);
            }
        }

        return quantity - left;
    }

    /// <summary>Adds items to one named slot: it must be empty or hold the same item with room for them all.</summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many; all of them are added or none.</param>
    /// <returns><see cref="InventoryVerdict.Ok"/> or the reason nothing was added.</returns>
    public InventoryVerdict AddToSlot(int slot, int itemId, int quantity)
    {
        if ((uint)slot >= (uint)_slots.Length)
        {
            return InventoryVerdict.InvalidSlot;
        }

        if (quantity <= 0)
        {
            return InventoryVerdict.InvalidQuantity;
        }

        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return InventoryVerdict.UnknownItem;
        }

        ItemSlot stack = _slots[slot];
        if (!stack.IsEmpty && stack.ItemId != itemId)
        {
            return InventoryVerdict.Blocked;
        }

        if ((stack.IsEmpty ? 0 : stack.Quantity) + quantity > item.MaxStack)
        {
            return InventoryVerdict.NoSpace;
        }

        _ = this.Fill(slot, itemId, item.MaxStack, quantity);
        return InventoryVerdict.Ok;
    }

    /// <summary>Removes items from any slots, highest slot first so the hotbar keeps its stacks longest.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="quantity">How many; all of them are removed or none.</param>
    /// <returns><see cref="InventoryVerdict.Ok"/> or the reason nothing was removed.</returns>
    public InventoryVerdict Remove(int itemId, int quantity)
    {
        if (quantity <= 0)
        {
            return InventoryVerdict.InvalidQuantity;
        }

        if (this.CountOf(itemId) < quantity)
        {
            return InventoryVerdict.NotEnough;
        }

        int left = quantity;
        for (int i = _slots.Length - 1; i >= 0 && left > 0; i--)
        {
            if (_slots[i].ItemId == itemId)
            {
                left -= this.Take(i, left);
            }
        }

        return InventoryVerdict.Ok;
    }

    /// <summary>Removes items from one slot.</summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="itemId">The item the caller expects there; a different item refuses.</param>
    /// <param name="quantity">How many.</param>
    /// <returns><see cref="InventoryVerdict.Ok"/> or the reason nothing was removed.</returns>
    public InventoryVerdict RemoveFromSlot(int slot, int itemId, int quantity)
    {
        if ((uint)slot >= (uint)_slots.Length)
        {
            return InventoryVerdict.InvalidSlot;
        }

        if (quantity <= 0)
        {
            return InventoryVerdict.InvalidQuantity;
        }

        ItemSlot stack = _slots[slot];
        if (stack.IsEmpty || stack.ItemId != itemId)
        {
            return InventoryVerdict.EmptySlot;
        }

        if (stack.Quantity < quantity)
        {
            return InventoryVerdict.NotEnough;
        }

        _ = this.Take(slot, quantity);
        return InventoryVerdict.Ok;
    }

    /// <summary>
    /// Moves a stack, or part of it, to another slot. Into an empty slot it moves (a part splits the
    /// stack); onto the same item it merges up to the stack limit; onto another item the whole stacks swap.
    /// </summary>
    /// <param name="from">Source slot.</param>
    /// <param name="to">Target slot.</param>
    /// <param name="quantity">How many to move; zero or less means the whole stack.</param>
    /// <returns><see cref="InventoryVerdict.Ok"/> or the reason nothing moved.</returns>
    public InventoryVerdict Move(int from, int to, int quantity = 0)
    {
        if ((uint)from >= (uint)_slots.Length || (uint)to >= (uint)_slots.Length || from == to)
        {
            return InventoryVerdict.InvalidSlot;
        }

        ItemSlot source = _slots[from];
        if (source.IsEmpty)
        {
            return InventoryVerdict.EmptySlot;
        }

        int count = quantity <= 0 ? source.Quantity : quantity;
        if (count > source.Quantity)
        {
            return InventoryVerdict.InvalidQuantity;
        }

        ItemSlot target = _slots[to];
        if (target.IsEmpty)
        {
            _ = this.Take(from, count);
            _slots[to] = new ItemSlot(source.ItemId, count);
            _changed[to] = true;
            return InventoryVerdict.Ok;
        }

        if (target.ItemId == source.ItemId)
        {
            _ = ItemCatalog.TryGet(source.ItemId, out ItemDefinition? item);
            int room = item!.MaxStack - target.Quantity;
            if (room <= 0)
            {
                return InventoryVerdict.NoSpace;
            }

            int moved = Math.Min(room, count);
            _ = this.Take(from, moved);
            _slots[to] = target with { Quantity = target.Quantity + moved };
            _changed[to] = true;
            return InventoryVerdict.Ok;
        }

        if (count != source.Quantity)
        {
            return InventoryVerdict.Blocked;
        }

        _slots[from] = target;
        _slots[to] = source;
        _changed[from] = true;
        _changed[to] = true;
        return InventoryVerdict.Ok;
    }

    /// <summary>
    /// Sorts the whole grid: the stacks of one item are merged up to the stack limit, then laid out from the first slot by item kind and then item id.
    /// Nothing is added or removed, so the layout fits whenever the grid held the items before; if it would not fit, nothing changes.
    /// </summary>
    /// <returns><see cref="InventoryVerdict.Ok"/>, or <see cref="InventoryVerdict.NoSpace"/> when the layout would not fit (then nothing changed).</returns>
    public InventoryVerdict Sort()
    {
        List<(int ItemId, int Quantity)> totals = [];
        foreach (ItemSlot stack in _slots)
        {
            if (!stack.IsEmpty)
            {
                AddTotal(totals, stack);
            }
        }

        totals.Sort(CompareForSort);

        ItemSlot[] layout = new ItemSlot[_slots.Length];
        int next = 0;
        foreach ((int itemId, int quantity) in totals)
        {
            _ = ItemCatalog.TryGet(itemId, out ItemDefinition? item);
            int maxStack = item!.MaxStack;
            for (int left = quantity; left > 0; left -= Math.Min(left, maxStack))
            {
                if (next == layout.Length)
                {
                    return InventoryVerdict.NoSpace;
                }

                layout[next++] = new ItemSlot(itemId, Math.Min(left, maxStack));
            }
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != layout[i])
            {
                _slots[i] = layout[i];
                _changed[i] = true;
            }
        }

        return InventoryVerdict.Ok;
    }

    /// <summary>Appends the slots changed since the last call to a caller-owned list and forgets them.</summary>
    /// <param name="into">List that receives the slot numbers, ascending.</param>
    public void TakeChanged(List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        for (int i = 0; i < _changed.Length; i++)
        {
            if (_changed[i])
            {
                into.Add(i);
                _changed[i] = false;
            }
        }
    }

    // Adds up to `limit` items to a slot that is empty or holds the same item; returns how many went in.
    private int Fill(int slot, int itemId, int maxStack, int limit)
    {
        int have = _slots[slot].ItemId == itemId ? _slots[slot].Quantity : 0;
        int add = Math.Min(maxStack - have, limit);
        if (add <= 0)
        {
            return 0;
        }

        _slots[slot] = new ItemSlot(itemId, have + add);
        _changed[slot] = true;
        return add;
    }

    // Takes up to `limit` items out of a slot; returns how many were taken.
    private int Take(int slot, int limit)
    {
        ItemSlot stack = _slots[slot];
        int take = Math.Min(stack.Quantity, limit);
        _slots[slot] = take == stack.Quantity ? default : stack with { Quantity = stack.Quantity - take };
        _changed[slot] = true;
        return take;
    }

    // Adds a stack's quantity to the running total of its item, creating the entry on first sight.
    private static void AddTotal(List<(int ItemId, int Quantity)> totals, ItemSlot stack)
    {
        for (int i = 0; i < totals.Count; i++)
        {
            if (totals[i].ItemId == stack.ItemId)
            {
                totals[i] = (stack.ItemId, totals[i].Quantity + stack.Quantity);
                return;
            }
        }

        totals.Add((stack.ItemId, stack.Quantity));
    }

    // Kind first (tools before seeds before produce, as the enum orders them), then id; an unknown item sorts last.
    private static int CompareForSort((int ItemId, int Quantity) a, (int ItemId, int Quantity) b)
    {
        int byKind = KindOf(a.ItemId).CompareTo(KindOf(b.ItemId));
        return byKind != 0 ? byKind : a.ItemId.CompareTo(b.ItemId);
    }

    private static int KindOf(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? (int)item.Kind : int.MaxValue;
}
