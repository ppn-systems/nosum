// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>Where a furnace stands. Values are append-only.</summary>
public enum FurnaceStateCode : byte
{
    /// <summary>Nothing is smelting and nothing waits to be collected.</summary>
    Idle = 0,

    /// <summary>Smelting; the bar is not ready yet.</summary>
    Working = 1,

    /// <summary>The bar is ready to be collected.</summary>
    Ready = 2,
}
