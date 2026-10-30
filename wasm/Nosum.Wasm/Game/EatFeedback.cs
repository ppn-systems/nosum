// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// The sparkle over the farmer when food restores energy: a few pale motes rising from the farmer's head. The socket callback only asks for it
/// (<see cref="Request"/>); the frame starts it, so the timing is the frame's and the drawing is rectangles in the frame's one submit (docs/adr/ADR-002).
/// With reduced motion the motes stay still and fade.
/// </summary>
internal static class EatFeedback
{
    /// <summary>How long the sparkle lasts, in milliseconds.</summary>
    public const long DurationMs = 700;

    private const int Motes = 6;
    private const int MoteSize = 4;

    // Pale mint, before the alpha (0xRRGGBBAA).
    private const int MoteRgb = 0xBBF7D0;

    private static int s_requested;
    private static bool s_active;
    private static long s_startedMs;

    /// <summary>Asks for the sparkle to start at the next frame. Safe from any thread.</summary>
    public static void Request() => Interlocked.Exchange(ref s_requested, 1);

    /// <summary>Records the sparkle of this frame; call once per frame, after the farmer is drawn.</summary>
    /// <param name="commands">Where the motes are recorded.</param>
    /// <param name="x">The farmer's head, x, unscaled screen pixels.</param>
    /// <param name="y">The farmer's head, y, unscaled screen pixels.</param>
    /// <param name="nowMs">The scene clock, milliseconds.</param>
    public static void Draw(RenderCommandBuffer commands, int x, int y, long nowMs)
    {
        if (Interlocked.Exchange(ref s_requested, 0) == 1)
        {
            s_active = true;
            s_startedMs = nowMs;
        }

        if (!s_active)
        {
            return;
        }

        long elapsed = nowMs - s_startedMs;
        if (elapsed < 0 || elapsed >= DurationMs)
        {
            s_active = false;
            return;
        }

        bool reduced = ViewOptions.ReduceMotion;
        int alpha = (int)(255 * (DurationMs - elapsed) / DurationMs);
        int rise = reduced ? 0 : (int)(elapsed * 22 / DurationMs);
        for (int i = 0; i < Motes; i++)
        {
            int sideways = ((i - (Motes / 2)) * 3) + ((i * 5) % 3);
            int lift = (i % 3) * 3;
            commands.FillRect(x + sideways, y - 10 - lift - rise, MoteSize, MoteSize, (MoteRgb << 8) | alpha);
        }
    }
}
