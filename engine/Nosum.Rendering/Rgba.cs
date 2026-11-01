// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>
/// Builds the colour value every fill and tint in the render command buffer takes: <c>0xRRGGBBAA</c>, red in the top byte and alpha in the low
/// byte (renderer.js, command -6). Use this helper for every tint so the byte order is written in one place; a raw literal in ARGB order decodes
/// to a near-transparent colour and draws nothing (docs/qa/combat-round5, PR #322).
/// </summary>
public static class Rgba
{
    /// <summary>Packs four 8-bit channels into <c>0xRRGGBBAA</c>. Each channel is clamped to 0..255. Allocates nothing.</summary>
    /// <param name="r">Red, 0 to 255.</param>
    /// <param name="g">Green, 0 to 255.</param>
    /// <param name="b">Blue, 0 to 255.</param>
    /// <param name="a">Alpha, 0 (transparent) to 255 (opaque).</param>
    /// <returns>The colour as <c>0xRRGGBBAA</c>.</returns>
    public static int From(int r, int g, int b, int a = 255)
    {
        uint packed = ((uint)Math.Clamp(r, 0, 255) << 24)
            | ((uint)Math.Clamp(g, 0, 255) << 16)
            | ((uint)Math.Clamp(b, 0, 255) << 8)
            | (uint)Math.Clamp(a, 0, 255);
        return unchecked((int)packed);
    }

    /// <summary>Returns <paramref name="rgba"/> with its alpha replaced by <paramref name="alpha"/> (clamped to 0..255).</summary>
    /// <param name="rgba">A colour as <c>0xRRGGBBAA</c>.</param>
    /// <param name="alpha">The new alpha, 0 to 255.</param>
    /// <returns>The colour with the new alpha.</returns>
    public static int WithAlpha(int rgba, int alpha) => unchecked((rgba & ~0xFF) | Math.Clamp(alpha, 0, 255));
}
