// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.ViewModels.Help;

/// <summary>One line of the help card: the keys (as printed on a keyboard) and what they do.</summary>
/// <param name="Keys">The key caps, for example <c>[Cap(GameAction.MoveUp), Cap(GameAction.MoveLeft), Cap(GameAction.MoveDown), Cap(GameAction.MoveRight)]</c>.</param>
/// <param name="Description">The localized description.</param>
public sealed record HelpEntry(IReadOnlyList<string> Keys, string Description);

/// <summary>A titled group of help lines.</summary>
/// <param name="Title">The localized heading.</param>
/// <param name="Entries">The lines.</param>
public sealed record HelpSection(string Title, IReadOnlyList<HelpEntry> Entries);

/// <summary>View model of the keyboard help card (key H or ?): every key the game answers to, with its localized description.</summary>
public sealed class HelpViewModel : PanelViewModel
{
    private readonly SettingsViewModel? _settings;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    /// <param name="settings">The settings whose key bindings the card shows; <see langword="null"/> for the default keys.</param>
    public HelpViewModel(IStringLocalizer<Language> text, SettingsViewModel? settings = null)
        : base(text)
    {
        _settings = settings;
        if (settings is not null)
        {
            settings.Changed += this.Raise;
        }
    }

    /// <summary>Gets the localized title of the card.</summary>
    public string Title => this.Text("Help_Title");

    /// <summary>Gets the sections of the card.</summary>
    public IReadOnlyList<HelpSection> Sections =>
    [
        new HelpSection(
            this.Text("Help_Section_Play"),
            [
                new HelpEntry([Cap(GameAction.MoveUp), Cap(GameAction.MoveLeft), Cap(GameAction.MoveDown), Cap(GameAction.MoveRight)], this.Text("Help_Move")),
                new HelpEntry([Cap(GameAction.Use), "Space"], this.Text("Help_Use")),
                new HelpEntry(["1-9"], this.Text("Help_Hotbar")),
            ]),
        new HelpSection(
            this.Text("Help_Section_Panels"),
            [
                new HelpEntry([Cap(GameAction.Inventory)], this.Text("Help_Inventory")),
                new HelpEntry([Cap(GameAction.Wardrobe)], this.Text("Help_Wardrobe")),
                new HelpEntry([Cap(GameAction.Trade)], this.Text("Help_Trade")),
                new HelpEntry([Cap(GameAction.Coop)], this.Text("Help_Coop")),
                new HelpEntry([Cap(GameAction.Profile)], this.Text("Help_Profile")),
                new HelpEntry([Cap(GameAction.Help), "?"], this.Text("Help_Help")),
                new HelpEntry(["Esc"], this.Text("Help_Close")),
            ]),
        new HelpSection(
            this.Text("Help_Section_Arrange"),
            [
                new HelpEntry([Cap(GameAction.Use)], this.Text("Help_Place")),
                new HelpEntry([Cap(GameAction.Rotate)], this.Text("Help_Rotate")),
                new HelpEntry([Cap(GameAction.Grab)], this.Text("Help_Grab")),
                new HelpEntry([Cap(GameAction.PickUp)], this.Text("Help_PickUp")),
            ]),
    ];

    private string Cap(GameAction action) => KeyBindings.CapOf((_settings?.Current.Bindings ?? KeyBindings.Default).CodeOf(action));
}
