// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Coop;

/// <summary>One part of the co-op window; the window shows one part at a time, chosen on its tab bar (docs/design/ui-standard.md section 3).</summary>
public enum CoopPart
{
    /// <summary>The members and the leave/invite row.</summary>
    Members,

    /// <summary>The shared warehouse: stock, deposit and withdraw.</summary>
    Warehouse,

    /// <summary>The upgrade tracks.</summary>
    Upgrades,

    /// <summary>The shared objectives.</summary>
    Objectives,

    /// <summary>The warehouse log.</summary>
    Log,
}
