// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Market;

/// <summary>How a browse page orders its listings (docs/adr/ADR-013 amendment 2026-11-01). Values are append-only; an unknown value reads as <see cref="Newest"/>.</summary>
public enum MarketSort : byte
{
    /// <summary>The newest listing first (the default, and what a client that does not ask for a sort gets).</summary>
    Newest = 0,

    /// <summary>The lowest price per unit first: stacks of different sizes compare on what one unit costs.</summary>
    UnitPriceLowest = 1,

    /// <summary>The highest price per unit first.</summary>
    UnitPriceHighest = 2,
}
