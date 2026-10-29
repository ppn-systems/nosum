// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Mines;

/// <summary>
/// The four depth tiers of the mine (docs/adr/ADR-070 D1): every ten floors share one biome, which the client shows as a palette of the
/// existing cave tiles and a music track. Cosmetic only: the server never reads it, so the rules here are pure and the client derives the same tier
/// from the floor it already knows.
/// </summary>
public enum MineBiome
{
    /// <summary>Floors 1 to 10: the earth caves.</summary>
    Earth = 0,

    /// <summary>Floors 11 to 20: the frozen caves.</summary>
    Frost = 1,

    /// <summary>Floors 21 to 30: the magma caves.</summary>
    Magma = 2,

    /// <summary>Floors 31 to 40: the ancient ruins.</summary>
    Ruins = 3,
}

/// <summary>The rule that puts a mine floor in its biome (docs/adr/ADR-070 D1).</summary>
public static class MineBiomeRules
{
    /// <summary>How many floors one biome spans.</summary>
    public const int FloorsPerBiome = 10;

    /// <summary>Returns the biome of a mine floor.</summary>
    /// <param name="floor">The floor, 1 to <see cref="MineRules.MaxFloor"/>.</param>
    /// <returns>Earth for floors 1 to 10, Frost for 11 to 20, Magma for 21 to 30 and Ruins for 31 to 40.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="floor"/> is not a mine floor.</exception>
    public static MineBiome Of(int floor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(floor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(floor, MineRules.MaxFloor);
        return (MineBiome)((floor - 1) / FloorsPerBiome);
    }
}
