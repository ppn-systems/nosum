// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.MapGen.Sheets;

/// <summary>Deterministic pseudo-random values from a position and a seed: the same inputs always give the same texture.</summary>
internal static class Noise
{
    /// <summary>Returns a value for a position and a seed.</summary>
    /// <param name="x">Column, zero or more.</param>
    /// <param name="y">Row, zero or more.</param>
    /// <param name="seed">A salt that separates the textures of one drawing.</param>
    /// <returns>An unsigned value spread over the whole range.</returns>
    public static uint Of(int x, int y, int seed)
    {
        unchecked
        {
            uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)seed * 2147483647u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
}
