// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.GroundItems;

/// <summary>
/// A small seeded random generator (SplitMix64) for yields: the same seed always gives the same sequence, so a roll can be
/// reproduced in a test. The server owns the seed; the client never rolls (docs/adr/ADR-021 D5). Not thread-safe: use one per roll.
/// </summary>
public sealed class GameRandom
{
    private ulong _state;

    /// <summary>Initializes the generator.</summary>
    /// <param name="seed">The seed.</param>
    public GameRandom(ulong seed) => _state = seed;

    /// <summary>Mixes numbers into one seed, so a roll can be keyed to what caused it (a node, a tile, a day).</summary>
    /// <param name="a">First number.</param>
    /// <param name="b">Second number.</param>
    /// <returns>A well-spread seed.</returns>
    public static ulong Mix(ulong a, ulong b)
    {
        GameRandom mixer = new(a ^ 0x9E3779B97F4A7C15UL);
        _ = mixer.NextUInt64();
        mixer._state ^= b;
        return mixer.NextUInt64();
    }

    /// <summary>Returns the next 64 random bits.</summary>
    /// <returns>The bits.</returns>
    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Returns an integer in a range.</summary>
    /// <param name="minInclusive">Smallest value.</param>
    /// <param name="maxInclusive">Largest value.</param>
    /// <returns>A value from <paramref name="minInclusive"/> to <paramref name="maxInclusive"/>.</returns>
    public int Range(int minInclusive, int maxInclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInclusive, minInclusive);
        ulong span = (ulong)((long)maxInclusive - minInclusive + 1);
        return (int)(minInclusive + (long)(this.NextUInt64() % span));
    }

    /// <summary>Rolls a percentage chance.</summary>
    /// <param name="percent">Chance, 0 to 100.</param>
    /// <returns><see langword="true"/> with that probability.</returns>
    public bool Chance(int percent) => percent > 0 && (percent >= 100 || this.Range(0, 99) < percent);
}
