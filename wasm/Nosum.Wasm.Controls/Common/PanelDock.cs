// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Controls.Common;

/// <summary>
/// Where a <c>GamePanel</c> used to sit on the screen. Kept so existing markup compiles; <c>GamePanel</c> ignores it now: a panel
/// sits where the <c>GameHud</c> dock region it is put in places it (docs/adr/ADR-016).
/// </summary>
public enum PanelDock
{
    /// <summary>In the normal flow.</summary>
    None = 0,

    /// <summary>Top-right corner.</summary>
    TopRight = 1,

    /// <summary>Top-left corner.</summary>
    TopLeft = 2,

    /// <summary>Centred.</summary>
    Center = 3,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft = 4,

    /// <summary>Top, centred.</summary>
    TopCenter = 5,
}
