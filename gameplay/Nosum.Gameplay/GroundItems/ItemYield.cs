// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.GroundItems;

/// <summary>One thing a node, crop or event yields: an item, a quantity range and the chance it drops at all.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Min">Fewest dropped when it drops.</param>
/// <param name="Max">Most dropped when it drops.</param>
/// <param name="ChancePercent">Chance it drops at all, 1 to 100.</param>
public readonly record struct ItemYield(int ItemId, int Min, int Max, int ChancePercent = 100)
{
    /// <summary>Creates a sure yield of a fixed quantity.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <returns>The yield.</returns>
    public static ItemYield Fixed(int itemId, int quantity) => new(itemId, quantity, quantity);

    /// <summary>Rolls the yield.</summary>
    /// <param name="random">The seeded generator.</param>
    /// <returns>How many items drop, zero when the chance failed.</returns>
    public int Roll(GameRandom random)
    {
        System.ArgumentNullException.ThrowIfNull(random);
        return random.Chance(this.ChancePercent) ? random.Range(this.Min, this.Max) : 0;
    }
}
