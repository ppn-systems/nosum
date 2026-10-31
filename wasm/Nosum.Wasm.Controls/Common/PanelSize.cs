// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Controls.Common;

/// <summary>The three widths of a game panel (docs/design/ui-standard.md section 3): S is 100 u, M is 160 u, L is 220 u.</summary>
public enum PanelSize
{
    /// <summary>100 u: a small window (a chest, a furnace, a pet, a visitor list).</summary>
    S,

    /// <summary>160 u: the usual window (a page of lists and forms).</summary>
    M,

    /// <summary>220 u: a wide window (the inventory with its side column, a shop or a market with a detail strip).</summary>
    L,

    /// <summary>340 u: a window with two grids side by side (the chest and the bag). Only for those panels, and stacked on a phone.</summary>
    XL,
}
