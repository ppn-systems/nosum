// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Placeable;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Chest;

/// <summary>
/// View model of the chest page (docs/adr/ADR-024 D7): the open chest's grid above the player's inventory, the Minecraft layout. Click a stack to pick it up, click a slot
/// to put it there (empty slot: move, same item: merge, other item: swap); shift-click sends a stack to the other side at once. Every move is a request; the grids change
/// when the server's pushes arrive.
/// </summary>
public sealed class ChestViewModel : PanelViewModel, IDisposable
{
    private readonly ChestChannel _chest;
    private readonly InventoryChannel _inventory;
    private readonly InventoryViewModel _bag;
    private readonly ItemDescriber _describer;
    private readonly GameMessagesViewModel? _messages;
    private bool _open;
    private ChestSide _pickedSide;
    private int _pickedSlot = -1;

    /// <summary>Initializes the view model.</summary>
    /// <param name="chest">The open chest's mirror.</param>
    /// <param name="inventory">The inventory mirror (the player's slots).</param>
    /// <param name="bag">The inventory view model (describes the player's slots).</param>
    /// <param name="describer">Describes the chest's items.</param>
    /// <param name="text">UI text.</param>
    /// <param name="messages">Shows a refusal on the game's message line when the page is not open; <see langword="null"/> for none.</param>
    public ChestViewModel(ChestChannel chest, InventoryChannel inventory, InventoryViewModel bag, ItemDescriber describer, IStringLocalizer<Language> text, GameMessagesViewModel? messages = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(chest);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(describer);

        _chest = chest;
        _inventory = inventory;
        _bag = bag;
        _describer = describer;
        _messages = messages;
        chest.Changed += this.OnChestChanged;
        chest.Closed += this.OnChestClosed;
        chest.Completed += this.OnCompleted;
        bag.Changed += this.Raise;
    }

    /// <summary>Raised with <see langword="true"/> when a chest opened and with <see langword="false"/> when it closed; the page shows or hides with it.</summary>
    public event Action<bool>? OpenChanged;

    /// <summary>Gets whether a chest is open.</summary>
    public bool IsOpen => _open;

    /// <summary>Gets the side of the picked stack, meaningful while <see cref="PickedSlot"/> is not negative.</summary>
    public ChestSide PickedSide => _pickedSide;

    /// <summary>Gets the slot of the picked stack, or -1.</summary>
    public int PickedSlot => _pickedSlot;

    /// <summary>Gets the picked slot index within the chest grid, or -1 when no chest slot is picked.</summary>
    public int PickedChestSlot => _pickedSlot >= 0 && _pickedSide == ChestSide.Chest ? _pickedSlot : -1;

    /// <summary>Gets the picked slot index within the player grid, or -1 when no player slot is picked.</summary>
    public int PickedPlayerSlot => _pickedSlot >= 0 && _pickedSide == ChestSide.Player ? _pickedSlot : -1;

    /// <summary>Gets the localized close button text.</summary>
    public string CloseLabel => this.Text("Inventory_Close");

    /// <summary>Gets the chest's slots as displays.</summary>
    public IReadOnlyList<SlotDisplay> ChestSlots
    {
        get
        {
            List<SlotDisplay> slots = new(ChestChannel.Capacity);
            for (int i = 0; i < ChestChannel.Capacity; i++)
            {
                SlotInfo info = _chest.SlotAt(i);
                slots.Add(_describer.Describe(i, info.ItemId, info.Quantity));
            }

            return slots;
        }
    }

    /// <summary>Gets the player's bag (the slots after the hotbar) as displays.</summary>
    public IReadOnlyList<SlotDisplay> BagSlots => [.. _bag.Storage];

    /// <summary>Gets the player's hotbar slots as displays.</summary>
    public IReadOnlyList<SlotDisplay> HotbarSlots => [.. _bag.Hotbar];

    /// <summary>Gets the localized label of the chest grid.</summary>
    public string ChestLabel => this.Text("Chest_Title");

    /// <summary>Gets the localized label of the player's grids.</summary>
    public string BagLabel => this.Text("Chest_YourBag");

    /// <summary>Gets the localized explanation of the clicks.</summary>
    public string Hint => this.Text("Chest_Hint");

    /// <summary>Handles a click on a slot of either grid.</summary>
    /// <param name="side">Which grid.</param>
    /// <param name="slot">The slot.</param>
    public void Click(ChestSide side, int slot)
    {
        if (_pickedSlot < 0)
        {
            if (this.IsOccupied(side, slot))
            {
                _pickedSide = side;
                _pickedSlot = slot;
                this.Raise();
            }

            return;
        }

        ChestSide fromSide = _pickedSide;
        int from = _pickedSlot;
        _pickedSlot = -1;
        if (fromSide != side || from != slot)
        {
            if (fromSide == ChestSide.Player && side == ChestSide.Player)
            {
                _inventory.Move(from, slot);
            }
            else
            {
                _chest.Move(fromSide, from, side, slot);
            }
        }

        this.Raise();
    }

    /// <summary>Shift-click: sends the whole stack to the first slot that fits on the other side.</summary>
    /// <param name="side">Which grid.</param>
    /// <param name="slot">The slot.</param>
    public void QuickMove(ChestSide side, int slot)
    {
        _pickedSlot = -1;
        if (this.IsOccupied(side, slot))
        {
            _chest.Move(side, slot, side == ChestSide.Chest ? ChestSide.Player : ChestSide.Chest, ChestChannel.AnySlot);
        }

        this.Raise();
    }

    /// <summary>Handles a click on a chest grid slot.</summary>
    /// <param name="slot">The chest slot index.</param>
    public void ClickChest(int slot) => this.Click(ChestSide.Chest, slot);

    /// <summary>Handles a click on a player inventory or hotbar slot.</summary>
    /// <param name="slot">The player slot index.</param>
    public void ClickPlayer(int slot) => this.Click(ChestSide.Player, slot);

    /// <summary>Shift-click on a chest grid slot: sends the stack to the player's inventory.</summary>
    /// <param name="slot">The chest slot index.</param>
    public void QuickMoveChest(int slot) => this.QuickMove(ChestSide.Chest, slot);

    /// <summary>Shift-click on a player slot: sends the stack to the chest.</summary>
    /// <param name="slot">The player slot index.</param>
    public void QuickMovePlayer(int slot) => this.QuickMove(ChestSide.Player, slot);

    /// <summary>Handles a click on a chest grid slot.</summary>
    /// <param name="slot">The chest slot index.</param>
    /// <returns>A completed task.</returns>
    public Task ClickChestAsync(int slot)
    {
        this.Click(ChestSide.Chest, slot);
        return Task.CompletedTask;
    }

    /// <summary>Handles a click on a player inventory or hotbar slot.</summary>
    /// <param name="slot">The player slot index.</param>
    /// <returns>A completed task.</returns>
    public Task ClickPlayerAsync(int slot)
    {
        this.Click(ChestSide.Player, slot);
        return Task.CompletedTask;
    }

    /// <summary>Shift-click on a chest grid slot: sends the stack to the player's inventory.</summary>
    /// <param name="slot">The chest slot index.</param>
    /// <returns>A completed task.</returns>
    public Task QuickMoveChestAsync(int slot)
    {
        this.QuickMove(ChestSide.Chest, slot);
        return Task.CompletedTask;
    }

    /// <summary>Shift-click on a player slot: sends the stack to the chest.</summary>
    /// <param name="slot">The player slot index.</param>
    /// <returns>A completed task.</returns>
    public Task QuickMovePlayerAsync(int slot)
    {
        this.QuickMove(ChestSide.Player, slot);
        return Task.CompletedTask;
    }

    /// <summary>Gets the localized label of the button that sorts the open chest.</summary>
    public string SortChestLabel => this.Text("Chest_SortChest");

    /// <summary>Gets the localized label of the button that sorts the player's bag.</summary>
    public string SortBagLabel => this.Text("Chest_SortBag");

    /// <summary>Sorts the open chest. A stack picked up on the page is put down first.</summary>
    public void SortChest()
    {
        _pickedSlot = -1;
        _chest.Sort();
        this.Raise();
    }

    /// <summary>Sorts the player's bag. A stack picked up on the page is put down first.</summary>
    public void SortBag()
    {
        _pickedSlot = -1;
        _inventory.Sort();
        this.Raise();
    }

    /// <summary>Closes the chest (the page's close button, Escape, or leaving).</summary>
    public void Close()
    {
        _pickedSlot = -1;
        _chest.Close();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _chest.Changed -= this.OnChestChanged;
        _chest.Closed -= this.OnChestClosed;
        _chest.Completed -= this.OnCompleted;
        _bag.Changed -= this.Raise;
    }

    private bool IsOccupied(ChestSide side, int slot) => (side == ChestSide.Chest ? _chest.SlotAt(slot) : _inventory.SlotAt(slot)).ItemId != 0;

    private void OnChestChanged()
    {
        if (!_open && _chest.IsOpen)
        {
            _open = true;
            this.ClearStatus();
            this.OpenChanged?.Invoke(true);
        }

        this.Raise();
    }

    private void OnChestClosed()
    {
        _pickedSlot = -1;
        if (_open)
        {
            _open = false;
            this.OpenChanged?.Invoke(false);
        }

        this.Raise();
    }

    private void OnCompleted(ChestStatus status)
    {
        if (status == ChestStatus.Ok)
        {
            return;
        }

        string text = this.Text("Chest_Status_" + status);
        if (_open)
        {
            this.SetStatus(text);
        }
        else
        {
            _messages?.Notify(text);
        }
    }
}
