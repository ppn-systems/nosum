// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>
/// View model of the inventory UI: the hotbar and the full inventory grid share it. It turns the
/// channel's mirror into localized slot displays (item names come from <c>Catalogue.resx</c> by the
/// item's stable key) and forwards selecting and moving to the server. Raises <see cref="Changed"/> on any
/// thread; a component marshals with <c>InvokeAsync(StateHasChanged)</c>.
/// </summary>
public sealed class InventoryViewModel : IObservableViewModel, IDisposable
{
    private readonly InventoryChannel _channel;
    private readonly IItemIconSource _icons;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly IStringLocalizer<Language> _text;
    private readonly NodeChannel? _nodes;
    private IReadOnlyList<SlotDisplay> _slots = [];
    private int _movingFrom = -1;
    private int _movingCount;
    private int _movingItem;

    /// <summary>Initializes the view model and starts following the channel.</summary>
    /// <param name="channel">The inventory mirror.</param>
    /// <param name="icons">Where item pictures are.</param>
    /// <param name="catalogue">Item names and descriptions.</param>
    /// <param name="text">UI text.</param>
    public InventoryViewModel(InventoryChannel channel, IItemIconSource icons, IStringLocalizer<Catalogue> catalogue, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(text);

        _channel = channel;
        _icons = icons;
        _catalogue = catalogue;
        _text = text;
        channel.Changed += this.Rebuild;
        UiCulture.Changed += this.Rebuild;
        channel.Refused += this.OnRefused;
        this.Rebuild();
    }

    /// <summary>Initializes the view model and also shows the water left in the watering can on its slot.</summary>
    /// <param name="channel">The inventory mirror.</param>
    /// <param name="icons">Where item pictures are.</param>
    /// <param name="catalogue">Item names and descriptions.</param>
    /// <param name="text">UI text.</param>
    /// <param name="nodes">The node channel, which carries the water of the can.</param>
    public InventoryViewModel(InventoryChannel channel, IItemIconSource icons, IStringLocalizer<Catalogue> catalogue, IStringLocalizer<Language> text, NodeChannel nodes)
        : this(channel, icons, catalogue, text)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        _nodes = nodes;
        nodes.ToolChanged += this.Rebuild;
        this.Rebuild();
    }

    /// <summary>Raised when the slots, the selection or the pending move changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when the server refused a move; the argument is the localized reason.</summary>
    public event Action<string>? Refused;

    /// <summary>Gets every slot of the inventory (empty until the snapshot arrived).</summary>
    public IReadOnlyList<SlotDisplay> Slots => _slots;

    /// <summary>Gets the first slots of the inventory, shown in the hotbar.</summary>
    public IEnumerable<SlotDisplay> Hotbar
    {
        get
        {
            for (int i = 0; i < Math.Min(InventoryLimits.HotbarSlots, _slots.Count); i++)
            {
                yield return _slots[i];
            }
        }
    }

    /// <summary>Gets the slots after the hotbar: the bag proper, shown above the hotbar row in the inventory window.</summary>
    public IEnumerable<SlotDisplay> Storage
    {
        get
        {
            for (int i = InventoryLimits.HotbarSlots; i < _slots.Count; i++)
            {
                yield return _slots[i];
            }
        }
    }

    /// <summary>Gets the stack in the held hotbar slot, or <see langword="null"/> when that slot is empty.</summary>
    public SlotDisplay? Held => (uint)this.SelectedSlot < (uint)_slots.Count && !_slots[this.SelectedSlot].IsEmpty ? _slots[this.SelectedSlot] : null;

    /// <summary>Gets the tool in the held hotbar slot, or <see cref="ToolKind.None"/> when that slot holds no tool.</summary>
    public ToolTier HeldTier => this.Held is { } held && ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item) ? item.Tier : ToolTier.Basic;

    /// <summary>Gets the tool in the held slot; <see cref="ToolKind.None"/> when the held item is not a tool.</summary>
    public ToolKind HeldTool => this.Held is { } held && ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item) ? item.Tool : ToolKind.None;

    /// <summary>Gets the heading of the held-item card.</summary>
    public string HandTitle => _text["Inventory_Hand"];

    /// <summary>Gets the text of the held-item card when the hand is empty.</summary>
    public string HandEmptyText => _text["Inventory_HandEmpty"];

    /// <summary>Gets the held hotbar slot.</summary>
    public int SelectedSlot => _channel.SelectedSlot;

    /// <summary>Gets the slot a move started from (the player clicked it and has not chosen the target yet), or -1.</summary>
    public int MovingFrom => _movingFrom;

    /// <summary>Gets how many of the picked stack the next target receives; zero means the whole stack (or nothing is picked).</summary>
    public int MovingCount => _movingCount;

    /// <summary>
    /// Gets the one-line hint under the bag: how to pick up, split and quick-move while nothing is picked, and where the picked stack goes
    /// while something is. The hint names the same clicks the page accepts, so a player does not have to guess them.
    /// </summary>
    public string Hint => _movingFrom < 0
        ? _text["Inventory_Hint"]
        : string.Format(CultureInfo.CurrentCulture, _text["Inventory_HintPicked"].Value, this.PickedQuantity);

    /// <summary>Gets the localized label of the hotbar.</summary>
    public string HotbarLabel => _text["Inventory_Hotbar"];

    /// <summary>Gets the localized label of the button that sorts the bag.</summary>
    public string SortLabel => _text["Inventory_Sort"];

    /// <summary>Gets the localized title of the inventory panel.</summary>
    public string Title => _text["Inventory_Title"];

    /// <summary>Gets the localized text of the close button.</summary>
    public string CloseText => _text["Inventory_Close"];

    /// <summary>Counts the total quantity of <paramref name="itemId"/> across all inventory slots.</summary>
    /// <param name="itemId">Item id from the item catalogue.</param>
    /// <returns>Total stacked quantity of the item carried by the player.</returns>
    public int CountOf(int itemId) => _channel.CountOf(itemId);

    /// <summary>Selects a hotbar slot (a click on the hotbar or a number key).</summary>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    public void Select(int slot) => _channel.Select(slot);

    /// <summary>
    /// A click in the inventory grid: the first click on an occupied slot picks it up, the second click on
    /// another slot asks the server to move it there (into an empty slot, merge or swap); a click on the
    /// same slot puts it down again.
    /// </summary>
    /// <param name="slot">The clicked slot.</param>
    public void Click(int slot)
    {
        if (_movingFrom < 0)
        {
            if (_channel.SlotAt(slot).ItemId != 0)
            {
                this.PickUp(slot, whole: true);
            }

            return;
        }

        int from = _movingFrom;
        int count = _movingCount;
        int item = _movingItem;
        this.PutDown();
        // A part of a stack only fills an empty slot or merges with its own item; the server would refuse the rest, so no request is sent.
        bool fits = count == 0 || _channel.SlotAt(slot).ItemId is 0 || _channel.SlotAt(slot).ItemId == item;
        if (from != slot && fits)
        {
            _channel.Move(from, slot, count);
        }

        this.Changed?.Invoke();
    }

    /// <summary>
    /// A right click on a bag slot: the first one picks up half of its stack (the rest stays), the next click places that part, as
    /// <see cref="Click"/> does for a whole stack. A right click while something is picked puts it down again.
    /// </summary>
    /// <param name="slot">The clicked slot.</param>
    public void RightClick(int slot)
    {
        if (_movingFrom >= 0)
        {
            this.PutDown();
            this.Changed?.Invoke();
            return;
        }

        SlotInfo info = _channel.SlotAt(slot);
        if (info.ItemId == 0)
        {
            return;
        }

        this.PickUp(slot, whole: info.Quantity <= 1);
    }

    /// <summary>
    /// A shift click: the stack moves to the other section (the hotbar goes to the bag, the bag to the hotbar) at the first slot that takes it:
    /// a slot of the same item with room first, then an empty one. When neither exists the bag is full for it and the page says so.
    /// </summary>
    /// <param name="slot">The clicked slot.</param>
    public void QuickMove(int slot)
    {
        this.PutDown();
        if ((uint)slot >= (uint)_channel.Capacity || _channel.SlotAt(slot).ItemId == 0)
        {
            this.Changed?.Invoke();
            return;
        }

        SlotInfo info = _channel.SlotAt(slot);

        bool fromHotbar = slot < InventoryLimits.HotbarSlots;
        int first = fromHotbar ? InventoryLimits.HotbarSlots : 0;
        int end = fromHotbar ? _channel.Capacity : InventoryLimits.HotbarSlots;
        int target = -1;
        int maxStack = ItemCatalog.TryGet(info.ItemId, out ItemDefinition? item) ? item.MaxStack : 1;
        for (int i = first; i < end && target < 0; i++)
        {
            SlotInfo other = _channel.SlotAt(i);
            if (other.ItemId == info.ItemId && other.Quantity < maxStack)
            {
                target = i;
            }
        }

        for (int i = first; i < end && target < 0; i++)
        {
            if (_channel.SlotAt(i).ItemId == 0)
            {
                target = i;
            }
        }

        if (target < 0)
        {
            this.Refused?.Invoke(_text["Inventory_Status_NoSpace"]);
        }
        else
        {
            _channel.Move(slot, target);
        }

        this.Changed?.Invoke();
    }

    /// <summary>Gets whether sorting keeps the hotbar as it is. On by default, so a sort never moves the tools a player has set up.</summary>
    public bool LockHotbar { get; private set; } = true;

    /// <summary>Gets the text of the hotbar lock toggle.</summary>
    public string LockHotbarLabel => _text["Inventory_LockHotbar"];

    /// <summary>Sets whether sorting keeps the hotbar as it is.</summary>
    /// <param name="locked">Whether the hotbar is locked.</param>
    public void SetLockHotbar(bool locked)
    {
        this.LockHotbar = locked;
        this.Changed?.Invoke();
    }

    /// <summary>Asks the server to sort the bag, keeping the hotbar when it is locked. A stack picked up for a move is put down first.</summary>
    public void Sort()
    {
        this.PutDown();
        _channel.Sort(this.LockHotbar);
        this.Changed?.Invoke();
    }

    // Starts a move from a slot: the whole stack, or half of it (a right click on a stack of two or more).
    private void PickUp(int slot, bool whole)
    {
        SlotInfo info = _channel.SlotAt(slot);
        _movingFrom = slot;
        _movingItem = info.ItemId;
        _movingCount = whole ? 0 : (info.Quantity + 1) / 2;
        this.Changed?.Invoke();
    }

    // Puts the picked stack down: nothing is sent to the server.
    private void PutDown()
    {
        _movingFrom = -1;
        _movingCount = 0;
        _movingItem = 0;
    }

    // How many items the pick holds: the part taken, or the whole stack.
    private int PickedQuantity
    {
        get
        {
            if (_movingFrom < 0)
            {
                return 0;
            }

            return _movingCount > 0 ? _movingCount : _channel.SlotAt(_movingFrom).Quantity;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Changed -= this.Rebuild;
        _channel.Refused -= this.OnRefused;
        UiCulture.Changed -= this.Rebuild;
        _nodes?.ToolChanged -= this.Rebuild;
    }

    private void OnRefused(InventoryStatus status)
    {
        string key = status switch
        {
            InventoryStatus.Blocked => "Inventory_Status_Blocked",
            InventoryStatus.NoSpace => "Inventory_Status_NoSpace",
            _ => "Inventory_Status_Failed",
        };
        this.Refused?.Invoke(_text[key]);
    }

    private void Rebuild()
    {
        int capacity = _channel.Capacity;
        int selected = _channel.SelectedSlot;
        List<SlotDisplay> slots = new(capacity);
        for (int i = 0; i < capacity; i++)
        {
            SlotInfo info = _channel.SlotAt(i);
            slots.Add(info.ItemId == 0 ? new SlotDisplay(i, 0, 0, string.Empty, string.Empty, null, i == selected) : this.Describe(i, info, i == selected));
        }

        _slots = slots;
        this.Changed?.Invoke();
    }

    private SlotDisplay Describe(int index, SlotInfo info, bool selected)
    {
        ItemIcon? icon = _icons.TryGet(info.ItemId, out ItemIcon found) ? found : null;
        if (!ItemCatalog.TryGet(info.ItemId, out ItemDefinition? item))
        {
            // An id this client does not know (a newer server): show the stack without text.
            return new SlotDisplay(index, info.ItemId, info.Quantity, string.Empty, string.Empty, icon, selected);
        }

        string name = _catalogue["Item_" + item.Key + "_Name"];
        string description = _catalogue["Item_" + item.Key + "_Description"];
        string tooltip = ItemTooltip.Build(item, name, description, info.Quantity, _text);
        if (item.Tool == ToolKind.WateringCan && _nodes is { } can)
        {
            int left = can.WaterLeft;
            int capacity = can.WaterCapacity;
            string water = string.Format(CultureInfo.CurrentCulture, _text["Inventory_Water"].Value, left, capacity);
            return new SlotDisplay(index, info.ItemId, info.Quantity, name, string.Join('\n', tooltip, water), icon, selected, (double)left / capacity, left.ToString(CultureInfo.CurrentCulture));
        }

        return new SlotDisplay(index, info.ItemId, info.Quantity, name, tooltip, icon, selected);
    }
}
