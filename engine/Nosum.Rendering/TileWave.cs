// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>
/// A shimmer for tiles that carry a trait (for example water): horizontal strips of the tile are
/// redrawn shifted sideways by a travelling sine wave, so the surface ripples without extra art.
/// Used when a map's tile sheets provide no animation for the surface.
/// </summary>
/// <param name="Trait">Tiles with any of these traits ripple.</param>
/// <param name="StripHeight">Height of one strip, pixels (divides the tile height).</param>
/// <param name="Amplitude">Largest sideways shift, pixels.</param>
/// <param name="Wavelength">Vertical distance between wave crests, world pixels.</param>
/// <param name="PeriodMs">Time for the wave to travel one wavelength, milliseconds.</param>
public readonly record struct TileWave(TileTraits Trait, int StripHeight, int Amplitude, int Wavelength, int PeriodMs)
{
    /// <summary>Gets a wave that does nothing.</summary>
    public static TileWave None => default;

    /// <summary>Gets whether the wave shifts anything.</summary>
    public bool IsActive => this.Trait != TileTraits.None && this.StripHeight > 0 && this.Amplitude > 0 && this.Wavelength > 0 && this.PeriodMs > 0;

    /// <summary>Returns the sideways shift of a strip at a world row and time. Does not allocate.</summary>
    /// <param name="worldY">World y of the strip, pixels.</param>
    /// <param name="timeMs">Animation clock, milliseconds.</param>
    /// <returns>Shift in pixels, between <c>-Amplitude</c> and <c>Amplitude</c>.</returns>
    public int Offset(int worldY, long timeMs)
    {
        double phase = ((double)worldY / this.Wavelength) - ((double)(timeMs % this.PeriodMs) / this.PeriodMs);
        return (int)Math.Round(this.Amplitude * Math.Sin(2 * Math.PI * phase));
    }
}
