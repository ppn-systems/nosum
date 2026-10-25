// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>Maps the volume a player sets (a slider, 0 to 100) to the gain the audio backend applies (docs/adr/ADR-039 D5).</summary>
public static class VolumeCurve
{
    /// <summary>Returns the gain for a slider value: quadratic, so the lower half of the slider stays usable (loudness is not linear in gain).</summary>
    /// <param name="percent">Slider value; outside 0 to 100 it is clamped.</param>
    /// <returns>Gain from 0 (silence) to 1 (full).</returns>
    public static double Gain(int percent)
    {
        double level = Math.Clamp(percent, 0, 100) / 100.0;
        return level * level;
    }

    /// <summary>Returns the gain for a slider value, 0 when everything is muted.</summary>
    /// <param name="percent">Slider value.</param>
    /// <param name="muted">Whether all sound is muted.</param>
    /// <returns>Gain from 0 to 1.</returns>
    public static double Gain(int percent, bool muted) => muted ? 0 : Gain(percent);
}
