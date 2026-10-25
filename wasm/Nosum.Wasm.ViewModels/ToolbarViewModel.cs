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

    /// <summary>The settings page: account, language and the key help (opened by the gear, <c>H</c> or <c>?</c>).</summary>
    Settings = 9,

    /// <summary>The visitors page: who may enter the player's farm, and visiting someone else (docs/adr/ADR-019).</summary>
    Visitors = 10,

    /// <summary>The Town board's goals page, opened with the action key at the board counter (not a toolbar button, docs/adr/ADR-030).</summary>
    Goals = 11,
}

/// <summary>One toolbar button.</summary>
/// <param name="Kind">The panel it opens.</param>
/// <param name="Label">The localized name.</param>
/// <param name="Hotkey">The key that does the same.</param>
/// <param name="Icon">The theme sprite of its menu tab, for example <c>tab-inventory</c>.</param>
public sealed record ToolbarItem(PanelKind Kind, string Label, string Hotkey, string Icon = "");

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

    /// <summary>Gets the tabs of the game menu, left to right. A new tab is one more entry here (plus its page in <c>Home.razor</c> and its sprite in <c>nosum-theme.js</c>).</summary>
    public IReadOnlyList<ToolbarItem> Items =>
    [
        new ToolbarItem(PanelKind.Inventory, this.Text("Toolbar_Inventory"), "I", "tab-inventory"),
        new ToolbarItem(PanelKind.Wardrobe, this.Text("Toolbar_Wardrobe"), "C", "tab-wardrobe"),
        new ToolbarItem(PanelKind.Market, this.Text("Toolbar_Market"), "M", "tab-market"),
        new ToolbarItem(PanelKind.Trade, this.Text("Toolbar_Trade"), "T", "tab-trade"),
        new ToolbarItem(PanelKind.Coop, this.Text("Toolbar_Coop"), "G", "tab-coop"),
        new ToolbarItem(PanelKind.Visitors, this.Text("Toolbar_Visitors"), string.Empty, "tab-visitors"),
        new ToolbarItem(PanelKind.Profile, this.Text("Toolbar_Profile"), "N", "tab-profile"),
        new ToolbarItem(PanelKind.Settings, this.Text("Toolbar_Settings"), "H", "tab-settings"),
    ];

    /// <summary>Gets the shop tab, shown only while a shop is open (opened at its counter, not from the menu).</summary>
    public ToolbarItem ShopItem => new(PanelKind.Shop, this.Text("Toolbar_Shop"), string.Empty, "tab-shop");

    /// <summary>Gets the Town board tab, shown only while the board is open (opened at its counter, not from the menu).</summary>
    public ToolbarItem GoalsItem => new(PanelKind.Goals, this.Text("Toolbar_Goals"), string.Empty, "tab-goals");
}
