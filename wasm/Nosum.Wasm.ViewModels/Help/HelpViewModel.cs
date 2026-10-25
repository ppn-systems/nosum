// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Help;

/// <summary>One line of the help card: the keys (as printed on a keyboard) and what they do.</summary>
/// <param name="Keys">The key caps, for example <c>["W", "A", "S", "D"]</c>.</param>
/// <param name="Description">The localized description.</param>
public sealed record HelpEntry(IReadOnlyList<string> Keys, string Description);

/// <summary>A titled group of help lines.</summary>
/// <param name="Title">The localized heading.</param>
/// <param name="Entries">The lines.</param>
public sealed record HelpSection(string Title, IReadOnlyList<HelpEntry> Entries);

/// <summary>View model of the keyboard help card (key H or ?): every key the game answers to, with its localized description.</summary>
public sealed class HelpViewModel : PanelViewModel
{
    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    public HelpViewModel(IStringLocalizer<Language> text)
        : base(text)
    {
    }

    /// <summary>Gets the localized title of the card.</summary>
    public string Title => this.Text("Help_Title");

    /// <summary>Gets the sections of the card.</summary>
    public IReadOnlyList<HelpSection> Sections =>
    [
        new HelpSection(
            this.Text("Help_Section_Play"),
            [
                new HelpEntry(["W", "A", "S", "D"], this.Text("Help_Move")),
                new HelpEntry(["E", "Space"], this.Text("Help_Use")),
                new HelpEntry(["E"], this.Text("Help_Counters")),
                new HelpEntry(["1-9"], this.Text("Help_Hotbar")),
            ]),
        new HelpSection(
            this.Text("Help_Section_Panels"),
            [
                new HelpEntry(["I"], this.Text("Help_Inventory")),
                new HelpEntry(["C"], this.Text("Help_Wardrobe")),
                new HelpEntry(["T"], this.Text("Help_Trade")),
                new HelpEntry(["G"], this.Text("Help_Coop")),
                new HelpEntry(["N"], this.Text("Help_Profile")),
                new HelpEntry(["H", "?"], this.Text("Help_Help")),
                new HelpEntry(["Esc"], this.Text("Help_Close")),
            ]),
        new HelpSection(
            this.Text("Help_Section_Arrange"),
            [
                new HelpEntry(["E"], this.Text("Help_Place")),
                new HelpEntry(["R"], this.Text("Help_Rotate")),
                new HelpEntry(["F"], this.Text("Help_Grab")),
                new HelpEntry(["Q"], this.Text("Help_PickUp")),
            ]),
    ];
}
