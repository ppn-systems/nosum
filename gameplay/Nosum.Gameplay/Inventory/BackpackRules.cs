// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Inventory;

/// <summary>The next backpack size a player may buy, and what it costs.</summary>
/// <param name="Capacity">Slots the backpack has after the purchase.</param>
/// <param name="Coins">Coins the purchase costs.</param>
public readonly record struct BackpackQuote(int Capacity, int Coins);

/// <summary>
/// The tailor's backpack upgrades (docs/adr/ADR-081). A backpack grows one row of the 9-column grid at a time, from
/// <see cref="InventoryLimits.DefaultCapacity"/> up to <see cref="MaxCapacity"/>, and each size has a price in coins. The sizes and prices are one
/// table here, so the server prices a purchase and the client reads the same numbers from the packet, never from its own copy. Pure rules.
/// </summary>
public static class BackpackRules
{
    /// <summary>The largest backpack, in slots (six rows of nine).</summary>
    public const int MaxCapacity = 54;

    // Each larger size, in ascending order, with what it costs. The first size above the current one is the next purchase.
    private static readonly BackpackQuote[] s_sizes =
    [
        new(45, 600),
        new(MaxCapacity, 1_000),
    ];

    /// <summary>Finds the size a backpack of <paramref name="capacity"/> slots can grow to next.</summary>
    /// <param name="capacity">The backpack's current size, in slots.</param>
    /// <param name="quote">The next size and its price; the default value when there is none.</param>
    /// <returns><see langword="false"/> when the backpack is already the largest size.</returns>
    public static bool TryNext(int capacity, out BackpackQuote quote)
    {
        foreach (BackpackQuote size in s_sizes)
        {
            if (size.Capacity > capacity)
            {
                quote = size;
                return true;
            }
        }

        quote = default;
        return false;
    }

    /// <summary>Tells whether a backpack may grow from one size to another: the target must be larger than the current size and no larger than <see cref="MaxCapacity"/>.</summary>
    /// <param name="current">The current size, in slots.</param>
    /// <param name="target">The size to grow to, in slots.</param>
    /// <returns><see langword="true"/> when the growth is allowed.</returns>
    public static bool MayGrow(int current, int target) => target > current && target <= MaxCapacity;
}
