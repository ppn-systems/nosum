// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
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
    private IReadOnlyList<SlotDisplay> _slots = [];
    private int _movingFrom = -1;

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
        channel.Refused += this.OnRefused;
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

    /// <summary>Gets the heading of the held-item card.</summary>
    public string HandTitle => _text["Inventory_Hand"];

    /// <summary>Gets the text of the held-item card when the hand is empty.</summary>
    public string HandEmptyText => _text["Inventory_HandEmpty"];

    /// <summary>Gets the held hotbar slot.</summary>
    public int SelectedSlot => _channel.SelectedSlot;

    /// <summary>Gets the slot a move started from (the player clicked it and has not chosen the target yet), or -1.</summary>
    public int MovingFrom => _movingFrom;

    /// <summary>Gets the localized label of the hotbar.</summary>
    public string HotbarLabel => _text["Inventory_Hotbar"];

    /// <summary>Gets the localized title of the inventory panel.</summary>
    public string Title => _text["Inventory_Title"];

    /// <summary>Gets the localized text of the close button.</summary>
    public string CloseText => _text["Inventory_Close"];

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
                _movingFrom = slot;
                this.Changed?.Invoke();
            }

            return;
        }

        int from = _movingFrom;
        _movingFrom = -1;
        if (from != slot)
        {
            _channel.Move(from, slot);
        }

        this.Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Changed -= this.Rebuild;
        _channel.Refused -= this.OnRefused;
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
        string tooltip = item.MaxStack > 1
            ? string.Join('\n', name, description, string.Format(CultureInfo.CurrentCulture, _text["Inventory_Quantity"].Value, info.Quantity))
            : string.Join('\n', name, description);
        return new SlotDisplay(index, info.ItemId, info.Quantity, name, tooltip, icon, selected);
    }
}
