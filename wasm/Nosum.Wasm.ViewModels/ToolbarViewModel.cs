// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels;

/// <summary>The panels the player can open over the game.</summary>
public enum PanelKind
{
    /// <summary>No panel.</summary>
    None = 0,

    /// <summary>The inventory.</summary>
    Inventory = 1,

    /// <summary>The market.</summary>
    Market = 2,

    /// <summary>The wardrobe.</summary>
    Wardrobe = 3,

    /// <summary>The trade window.</summary>
    Trade = 4,

    /// <summary>The co-op.</summary>
    Coop = 5,

    /// <summary>The keyboard help card.</summary>
    Help = 6,

    /// <summary>The profile (nickname).</summary>
    Profile = 7,

    /// <summary>A shop, opened with the action key at its counter (not a toolbar button).</summary>
    Shop = 8,
}

/// <summary>One toolbar button.</summary>
/// <param name="Kind">The panel it opens.</param>
/// <param name="Label">The localized name.</param>
/// <param name="Hotkey">The key that does the same.</param>
public sealed record ToolbarItem(PanelKind Kind, string Label, string Hotkey);

/// <summary>View model of the toolbar of panel buttons; knows which panel is open.</summary>
public sealed class ToolbarViewModel : PanelViewModel
{
    private PanelKind _active;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    public ToolbarViewModel(IStringLocalizer<Language> text)
        : base(text)
    {
    }

    /// <summary>Gets the accessible name of the toolbar.</summary>
    public string Label => this.Text("Toolbar_Label");

    /// <summary>Gets the panel that is open.</summary>
    public PanelKind Active
    {
        get => _active;
        set
        {
            if (_active != value)
            {
                _active = value;
                this.Raise();
            }
        }
    }

    /// <summary>Gets the buttons.</summary>
    public IReadOnlyList<ToolbarItem> Items =>
    [
        new ToolbarItem(PanelKind.Inventory, this.Text("Toolbar_Inventory"), "I"),
        new ToolbarItem(PanelKind.Market, this.Text("Toolbar_Market"), "M"),
        new ToolbarItem(PanelKind.Wardrobe, this.Text("Toolbar_Wardrobe"), "C"),
        new ToolbarItem(PanelKind.Trade, this.Text("Toolbar_Trade"), "T"),
        new ToolbarItem(PanelKind.Coop, this.Text("Toolbar_Coop"), "G"),
        new ToolbarItem(PanelKind.Help, this.Text("Toolbar_Help"), "H"),
        new ToolbarItem(PanelKind.Profile, this.Text("Toolbar_Profile"), "N"),
    ];
}
