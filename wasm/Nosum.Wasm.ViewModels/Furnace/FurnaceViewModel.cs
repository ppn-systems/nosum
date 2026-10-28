// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Furnace;

/// <summary>One ore the furnace can smelt, with what the player has and whether a job can start with it.</summary>
/// <param name="Item">The ore as a slot (quantity is what the player holds).</param>
/// <param name="Label">The localized button label ("Smelt Copper ore").</param>
/// <param name="CanStart">Whether a job can start now: idle, enough ore and coal.</param>
public sealed record OreChoice(SlotDisplay Item, string Label, bool CanStart);

/// <summary>
/// View model of the furnace page (docs/adr/ADR-024 D7): idle (pick an ore), working (countdown) or ready (collect). The state and the countdown come from the channel;
/// the server decides every start and collect.
/// </summary>
public sealed class FurnaceViewModel : PanelViewModel, IDisposable
{
    private static readonly int[] s_ores = [ItemCatalog.CopperOre, ItemCatalog.IronOre];

    private readonly FurnaceChannel _furnaces;
    private readonly InventoryChannel _inventory;
    private readonly ItemDescriber _describer;
    private readonly GameMessagesViewModel? _messages;
    private int _objectId;

    /// <summary>Initializes the view model.</summary>
    /// <param name="furnaces">The furnace mirror.</param>
    /// <param name="inventory">The inventory mirror: ore and coal the player has.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    /// <param name="messages">Shows a refusal on the game's message line when the page closed with it; <see langword="null"/> for none.</param>
    public FurnaceViewModel(FurnaceChannel furnaces, InventoryChannel inventory, ItemDescriber describer, IStringLocalizer<Language> text, GameMessagesViewModel? messages = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(furnaces);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);

        _furnaces = furnaces;
        _inventory = inventory;
        _describer = describer;
        _messages = messages;
        furnaces.Changed += this.Raise;
        furnaces.Completed += this.OnCompleted;
        inventory.Changed += this.Raise;
    }

    /// <summary>Raised with <see langword="true"/> when the page opened and with <see langword="false"/> when it closed.</summary>
    public event Action<bool>? OpenChanged;

    /// <summary>Gets the id of the furnace shown; zero when none.</summary>
    public int ObjectId => _objectId;

    /// <summary>Gets whether the page is open.</summary>
    public bool IsOpen => _objectId != 0;

    /// <summary>Gets where the furnace stands now (idle, working or ready).</summary>
    public FurnaceStateCode State => _furnaces.StateOf(_objectId, out _, out _);

    /// <summary>Gets whether the furnace has a finished bar ready to collect.</summary>
    public bool IsReady => this.State == FurnaceStateCode.Ready;

    /// <summary>Gets the localized title of the furnace panel.</summary>
    public string Title => this.Text("Furnace_Title");

    /// <summary>Gets the localized close button label.</summary>
    public string CloseLabel => this.Text("Inventory_Close");

    /// <summary>Gets the localized one-line description of the furnace's state, with the countdown while working.</summary>
    public string StateText
    {
        get
        {
            FurnaceStateCode state = _furnaces.StateOf(_objectId, out int ore, out int remainingMs);
            return state switch
            {
                FurnaceStateCode.Working => this.Text("Furnace_Working", _describer.NameOf(ore), FormatRemaining(remainingMs)),
                FurnaceStateCode.Ready => this.Text("Furnace_Ready"),
                _ => this.Text("Furnace_Idle", FurnaceRules.OreCost, FurnaceRules.CoalCost, FurnaceRules.GameMinutes),
            };
        }
    }

    /// <summary>Gets the ores a job can start with.</summary>
    public IReadOnlyList<OreChoice> Ores
    {
        get
        {
            bool idle = this.State == FurnaceStateCode.Idle;
            int coal = _inventory.CountOf(ItemCatalog.Coal);
            List<OreChoice> choices = [];
            foreach (int ore in s_ores)
            {
                int have = _inventory.CountOf(ore);
                choices.Add(new OreChoice(_describer.Describe(ore, ore, have), this.Text("Furnace_Start", _describer.NameOf(ore)), idle && have >= FurnaceRules.OreCost && coal >= FurnaceRules.CoalCost));
            }

            return choices;
        }
    }

    /// <summary>Gets the localized text of the collect button.</summary>
    public string CollectLabel => this.Text("Furnace_Collect");

    /// <summary>Opens the page for a furnace (the action key facing it).</summary>
    /// <param name="objectId">The furnace's id.</param>
    public void Open(int objectId)
    {
        if (objectId == 0)
        {
            return;
        }

        bool wasOpen = this.IsOpen;
        _objectId = objectId;
        this.ClearStatus();
        if (!wasOpen)
        {
            this.OpenChanged?.Invoke(true);
        }
    }

    /// <summary>Closes the page.</summary>
    public void Close()
    {
        if (_objectId != 0)
        {
            _objectId = 0;
            this.OpenChanged?.Invoke(false);
            this.Raise();
        }
    }

    /// <summary>Asks the server to start a job with an ore; does nothing when the rules say it cannot work.</summary>
    /// <param name="oreItemId">The ore.</param>
    public void Start(int oreItemId)
    {
        foreach (OreChoice choice in this.Ores)
        {
            if (choice.Item.ItemId == oreItemId && choice.CanStart)
            {
                _furnaces.Start(_objectId, oreItemId);
                return;
            }
        }
    }

    /// <summary>Asks the server for the finished bar.</summary>
    public void Collect()
    {
        if (this.State == FurnaceStateCode.Ready)
        {
            _furnaces.Collect(_objectId);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _furnaces.Changed -= this.Raise;
        _furnaces.Completed -= this.OnCompleted;
        _inventory.Changed -= this.Raise;
    }

    // Minutes and seconds of real time ("12:05").
    private static string FormatRemaining(int remainingMs)
    {
        int seconds = (remainingMs + 999) / 1000;
        return string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    private void OnCompleted(FurnaceStatus status)
    {
        if (status == FurnaceStatus.Ok)
        {
            this.ClearStatus();
            return;
        }

        string text = this.Text("Furnace_Status_" + status);
        if (status is FurnaceStatus.OutOfReach or FurnaceStatus.NotYours or FurnaceStatus.UnknownObject)
        {
            // Out of reach or not ours: the page cannot do anything, so it closes and the message line says why.
            this.Close();
            _messages?.Notify(text);
            return;
        }

        this.SetStatus(text);
    }
}
