// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Rendering;

/// <summary>
/// Camera shake for combat impacts (docs/adr/ADR-076): a decaying offset that re-jitters every <see cref="BeatMs"/>. Pure and
/// allocation-free; the same inputs always give the same offset, so a frame can be reproduced and tested.
/// </summary>
public static class CameraShake
{
    /// <summary>How long one jitter position holds, in milliseconds.</summary>
    public const int BeatMs = 32;

    /// <summary>Returns the view offset of a shake that started <paramref name="elapsedMs"/> ago.</summary>
    /// <param name="elapsedMs">Milliseconds since the impact.</param>
    /// <param name="durationMs">How long the shake lasts.</param>
    /// <param name="amplitudePx">Largest offset in world pixels, reached at the impact and decaying linearly to zero.</param>
    /// <param name="dx">Horizontal offset, world pixels.</param>
    /// <param name="dy">Vertical offset, world pixels.</param>
    public static void Offset(int elapsedMs, int durationMs, int amplitudePx, out int dx, out int dy)
    {
        if (elapsedMs < 0 || elapsedMs >= durationMs || durationMs <= 0 || amplitudePx <= 0)
        {
            dx = 0;
            dy = 0;
            return;
        }

        int strength = amplitudePx * (durationMs - elapsedMs) / durationMs;
        int beat = elapsedMs / BeatMs;
        int span = (strength * 2) + 1;
        dx = (Hash(beat * 2) % span) - strength;
        dy = (Hash((beat * 2) + 1) % span) - strength;
    }

    private static int Hash(int n)
    {
        unchecked
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)(h & 0x7fffffff);
        }
    }
}
