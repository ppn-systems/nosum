// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Core;

/// <summary>
/// Directional input held down this frame, as bit flags so the platform can pass it as one
/// integer per frame.
/// </summary>
[Flags]
public enum InputState
{
    /// <summary>Nothing held.</summary>
    None = 0,

    /// <summary>Up (W / arrow up).</summary>
    Up = 1,

    /// <summary>Down (S / arrow down).</summary>
    Down = 2,

    /// <summary>Left (A / arrow left).</summary>
    Left = 4,

    /// <summary>Right (D / arrow right).</summary>
    Right = 8,
    /// <summary>Run (Shift, or the touch run toggle): moves faster and spends energy (docs/adr/ADR-085).</summary>
    Run = 16,
}
