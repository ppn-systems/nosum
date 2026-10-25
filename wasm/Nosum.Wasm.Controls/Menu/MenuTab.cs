// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Controls.Menu;

/// <summary>One tab of the game menu.</summary>
/// <param name="Id">The id handed back to <see cref="GameMenu.OnSelect"/> (the page maps it to a panel).</param>
/// <param name="Label">The localized name (tooltip and window title).</param>
/// <param name="Icon">The theme sprite, for example <c>tab-inventory</c> (see <c>nosum-theme.js</c>).</param>
/// <param name="Hotkey">The key that opens the tab, or empty.</param>
public sealed record MenuTab(int Id, string Label, string Icon, string Hotkey);
