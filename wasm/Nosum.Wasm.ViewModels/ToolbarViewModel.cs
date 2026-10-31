// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.ViewModels;

/// <summary>The panels the player can open over the game.</summary>
public enum PanelKind
{
    /// <summary>No panel.</summary>
    None = 0,

    /// <summary>The inventory.</summary>
    Inventory = 1,

    /// <summary>The market, opened from the trader's dialog box in Town (docs/adr/ADR-034, ADR-043); not a menu tab.</summary>
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

    /// <summary>A shop, opened from a merchant's dialog box (docs/adr/ADR-043); not a menu tab.</summary>
    Shop = 8,

    /// <summary>The settings page: account, language and the key help (opened by the gear, <c>H</c> or <c>?</c>).</summary>
    Settings = 9,

    /// <summary>The visitors page: who may enter the player's farm, and visiting someone else (docs/adr/ADR-019).</summary>
    Visitors = 10,

    /// <summary>The friends page: friends, requests, the friends-may-visit switch and the block list (docs/adr/ADR-029).</summary>
    Friends = 15,

    /// <summary>The mail page (docs/adr/ADR-029): a tab of its own, with its own HUD icon and unread badge (docs/adr/ADR-043 D4).</summary>
    Mail = 16,

    /// <summary>The skills page: energy and the three skill levels (docs/adr/ADR-023).</summary>
    Skills = 11,

    /// <summary>The crafting page (docs/adr/ADR-024).</summary>
    Crafting = 12,

    /// <summary>The open chest (docs/adr/ADR-024); a dialog of its own while a chest is open, not a menu tab (docs/adr/ADR-043).</summary>
    Chest = 13,

    /// <summary>The furnace (docs/adr/ADR-024); a dialog of its own while a furnace is open, not a menu tab (docs/adr/ADR-043).</summary>
    Furnace = 14,

    /// <summary>The Town board's goals page, opened from the board keeper's dialog box (docs/adr/ADR-030, ADR-043); not a menu tab.</summary>
    Goals = 17,

    /// <summary>The world map: every public map as a card, the current one highlighted (Tab or the HUD button).</summary>
    WorldMap = 18,

    /// <summary>The collection journal: every item discovered so far, silhouettes for the rest (the J key or the menu).</summary>
    Journal = 19,

    /// <summary>The pet panel: the companion's name, mood and petting, or the adoption form on the own farm (docs/adr/ADR-075 D3-D4).</summary>
    Pet = 20,

    /// <summary>The game menu: one tile per panel that has no button in the HUD's menu column (docs/design/ui-standard.md section 2).</summary>
    Menu = 21,
}

/// <summary>One toolbar button.</summary>
/// <param name="Kind">The panel it opens.</param>
/// <param name="Label">The localized name.</param>
/// <param name="Hotkey">The key that does the same.</param>
/// <param name="Icon">The theme sprite of its menu tab, for example <c>tab-inventory</c>.</param>
public sealed record ToolbarItem(PanelKind Kind, string Label, string Hotkey, string Icon = "");

/// <summary>View model of the toolbar of panel buttons; knows which panel is open.</summary>
public sealed class ToolbarViewModel : PanelViewModel, IDisposable
{
    private readonly SettingsViewModel? _settings;
    private PanelKind _active;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    /// <param name="settings">The settings whose key bindings the tabs show; <see langword="null"/> for the default keys.</param>
    public ToolbarViewModel(IStringLocalizer<Language> text, SettingsViewModel? settings = null)
        : base(text)
    {
        _settings = settings;
        if (settings is not null)
        {
            settings.Changed += this.Raise;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_settings is not null)
        {
            _settings.Changed -= this.Raise;
        }
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

    /// <summary>The panels with a button in the HUD's menu column, top to bottom. The column holds at most 8 buttons, the last one is the Menu button
    /// (docs/design/ui-standard.md section 2): a panel not listed here is a tile of the game menu instead.</summary>
    public static readonly IReadOnlyList<PanelKind> ColumnKinds =
    [
        PanelKind.Inventory,
        PanelKind.Wardrobe,
        PanelKind.Crafting,
        PanelKind.Trade,
        PanelKind.Coop,
        PanelKind.Friends,
        PanelKind.Pet,
    ];

    /// <summary>Gets the tabs of the game menu, left to right. A new tab is one more entry here (plus its page in <c>Home.razor</c> and its sprite in <c>nosum-theme.js</c>).</summary>
    public IReadOnlyList<ToolbarItem> Items =>
    [
        new ToolbarItem(PanelKind.Inventory, this.Text("Toolbar_Inventory"), Key(GameAction.Inventory), "tab-inventory"),
        new ToolbarItem(PanelKind.Wardrobe, this.Text("Toolbar_Wardrobe"), Key(GameAction.Wardrobe), "tab-wardrobe"),
        new ToolbarItem(PanelKind.Crafting, this.Text("Toolbar_Crafting"), string.Empty, "tab-shop"),
        new ToolbarItem(PanelKind.Trade, this.Text("Toolbar_Trade"), Key(GameAction.Trade), "tab-trade"),
        new ToolbarItem(PanelKind.Coop, this.Text("Toolbar_Coop"), Key(GameAction.Coop), "tab-coop"),
        new ToolbarItem(PanelKind.Visitors, this.Text("Toolbar_Visitors"), string.Empty, "tab-visitors"),
        new ToolbarItem(PanelKind.Friends, this.Text("Toolbar_Friends"), string.Empty, "tab-friends"),
        new ToolbarItem(PanelKind.Mail, this.Text("Toolbar_Mail"), string.Empty, "tab-mail"),
        new ToolbarItem(PanelKind.Skills, this.Text("Toolbar_Skills"), string.Empty, "tab-skills"),
        new ToolbarItem(PanelKind.Pet, this.Text("Toolbar_Pet"), string.Empty, "tab-pet"),
        new ToolbarItem(PanelKind.Profile, this.Text("Toolbar_Profile"), Key(GameAction.Profile), "tab-profile"),
        new ToolbarItem(PanelKind.WorldMap, this.Text("Toolbar_WorldMap"), Key(GameAction.WorldMap), "tab-market"),
        new ToolbarItem(PanelKind.Journal, this.Text("Toolbar_Journal"), Key(GameAction.Journal), "tab-goals"),
        new ToolbarItem(PanelKind.Settings, this.Text("Toolbar_Settings"), Key(GameAction.Help), "tab-settings"),
    ];

    /// <summary>Gets the buttons of the HUD's menu column, top to bottom (the Menu button is added by the HUD, after them).</summary>
    public IReadOnlyList<ToolbarItem> ColumnItems
    {
        get
        {
            List<ToolbarItem> column = [];
            foreach (ToolbarItem item in this.Items)
            {
                if (ColumnKinds.Contains(item.Kind))
                {
                    column.Add(item);
                }
            }

            return column;
        }
    }

    /// <summary>Gets the tiles of the game menu: every panel of <see cref="Items"/> that is not in the menu column (Mail and Settings sit in the HUD's top row).</summary>
    public IReadOnlyList<ToolbarItem> MenuItems
    {
        get
        {
            List<ToolbarItem> tiles = [];
            foreach (ToolbarItem item in this.Items)
            {
                if (!ColumnKinds.Contains(item.Kind) && item.Kind is not (PanelKind.Mail or PanelKind.Settings))
                {
                    tiles.Add(item);
                }
            }

            return tiles;
        }
    }

    /// <summary>Gets the localized name of the Menu button and its window.</summary>
    public string MenuLabel => this.Text("Toolbar_Menu");

    private string Key(GameAction action) => KeyBindings.CapOf((_settings?.Current.Bindings ?? KeyBindings.Default).CodeOf(action));

    /// <summary>Gets the tooltip of the mail icon in the HUD: "Mail", with the unread count when there is mail to read (docs/adr/ADR-043 D4).</summary>
    /// <param name="unread">Unread mails.</param>
    /// <returns>The localized tooltip.</returns>
    public string MailTip(int unread) => unread > 0 ? this.Text("Toolbar_Mail") + " - " + this.Text("Toolbar_MailUnread", unread) : this.Text("Toolbar_Mail");

    /// <summary>Gets the tooltip of the settings cog in the HUD: only "Settings", the cog holds nothing else (docs/adr/ADR-043 D4).</summary>
    public string SettingsTip => this.Text("Toolbar_Settings");

    /// <summary>Gets the localized window title for a panel kind.</summary>
    /// <param name="kind">The panel kind.</param>
    /// <returns>The localized title.</returns>
    public string TitleOf(PanelKind kind)
    {
        foreach (ToolbarItem item in this.Items)
        {
            if (item.Kind == kind)
            {
                return item.Label;
            }
        }

        return string.Empty;
    }
}
