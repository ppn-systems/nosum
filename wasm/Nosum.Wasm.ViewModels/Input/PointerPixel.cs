// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Input;

/// <summary>
/// Turns a pointer position from the browser into the whole pixel the frame takes (docs/adr/ADR-100 D1). A scaled or emulated screen reports
/// fractional CSS pixels (for example 466.14); passed to an <c>int</c> parameter of a [JSExport], that value throws in the runtime and the frame loop
/// stops at the first pointer move.
/// </summary>
public static class PointerPixel
{
    /// <summary>
    /// Rounds a CSS pixel position to a whole pixel, halves away from zero. A value that is not finite is taken as zero.
    /// </summary>
    /// <param name="cssPixels">The position as the browser reports it.</param>
    /// <returns>The whole pixel, within the range of an <c>int</c>.</returns>
    public static int Of(double cssPixels)
    {
        if (!double.IsFinite(cssPixels))
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(cssPixels, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
    }
}
