// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Nodes;

/// <summary>
/// A tiny deterministic random source (SplitMix64) for everything the server rolls about nodes: initial placement, yields, regrowth.
/// The same seed always gives the same sequence on every machine and runtime, so tests can pin outcomes and a rolled yield can be
/// reproduced from its inputs. The client never rolls (docs/adr/ADR-021 D5).
/// </summary>
public struct NodeRandom
{
    private ulong _state;

    /// <summary>Initializes the source.</summary>
    /// <param name="seed">The seed.</param>
    public NodeRandom(ulong seed) => _state = seed;

    /// <summary>Returns a stable 64-bit hash of a string (FNV-1a), for seeding from an instance key.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    public static ulong HashOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Mixes values into a seed, so a roll depends on where and what it is for.</summary>
    /// <param name="seed">The base seed.</param>
    /// <param name="a">First value.</param>
    /// <param name="b">Second value.</param>
    /// <returns>A new seed.</returns>
    public static ulong Mix(ulong seed, long a, long b)
    {
        NodeRandom random = new(seed ^ ((ulong)a * 0x9E3779B97F4A7C15UL));
        _ = random.NextUInt64();
        random._state ^= (ulong)b * 0xC2B2AE3D27D4EB4FUL;
        return random.NextUInt64();
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

    /// <summary>Returns a number in a range.</summary>
    /// <param name="maxExclusive">Upper bound, greater than zero.</param>
    /// <returns>A number from zero to <paramref name="maxExclusive"/> minus one.</returns>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)(this.NextUInt64() % (ulong)maxExclusive);
    }

    /// <summary>Returns whether a roll out of 1000 lands under a chance.</summary>
    /// <param name="permille">Chance out of 1000.</param>
    /// <returns><see langword="true"/> when the roll succeeds.</returns>
    public bool Chance(int permille) => permille >= 1000 || (permille > 0 && this.Next(1000) < permille);
}
