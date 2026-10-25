// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;

namespace Nosum.Wasm.Game;

/// <summary>
/// The settings the frame reads (docs/adr/ADR-033): set from the settings page, read by the scene each frame as plain integers, so the frame
/// takes no lock and allocates nothing.
/// </summary>
internal static class ViewOptions
{
    private static int s_scale;
    private static int s_showNames = 1;
    private static int s_reduceMotion;

    /// <summary>Gets the zoom the player picked, 2 to 6, or 0 to pick it from the window size.</summary>
    public static int Scale => Volatile.Read(ref s_scale);

    /// <summary>Gets whether names are drawn over the farmers' heads.</summary>
    public static bool ShowNames => Volatile.Read(ref s_showNames) != 0;

    /// <summary>Gets whether weather particles are left out.</summary>
    public static bool ReduceMotion => Volatile.Read(ref s_reduceMotion) != 0;

    /// <summary>Sets the options.</summary>
    /// <param name="scale">The zoom, or 0 for automatic.</param>
    /// <param name="showNames">Whether names are drawn.</param>
    /// <param name="reduceMotion">Whether motion is reduced.</param>
    public static void Set(int scale, bool showNames, bool reduceMotion)
    {
        Volatile.Write(ref s_scale, scale);
        Volatile.Write(ref s_showNames, showNames ? 1 : 0);
        Volatile.Write(ref s_reduceMotion, reduceMotion ? 1 : 0);
    }
}
