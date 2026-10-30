// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Weather;

/// <summary>
/// The lightning of a storm as a pure function of the server clock: one strike per <see cref="SlotMs"/> window, at a moment picked from the window's
/// number. Every player sees the same flash and hears the thunder at the same server time, and nothing is sent. The client draws the flash and plays
/// the thunder; no game state depends on it (docs/adr/ADR-027 D1 for the same seeded-hash approach to the weather).
/// </summary>
public static class LightningRules
{
    /// <summary>Length of one strike window, in milliseconds: a strike falls in each, so the gap between two strikes averages this long.</summary>
    public const long SlotMs = 40_000;

    /// <summary>How long after a strike the thunder is due, in milliseconds: light first, then sound.</summary>
    public const int ThunderDelayMs = 450;

    /// <summary>How long the thunder stays due after its time, so a frame that finds it late still plays it and an old strike does not.</summary>
    public const int ThunderWindowMs = 1500;

    /// <summary>How long one flash lasts, in milliseconds.</summary>
    public const int FlashMs = 320;

    private const int FullPeakAlpha = 210;
    private const int SecondPeakAlpha = 170;
    private const int ReducedPeakAlpha = 46;

    /// <summary>Returns the window a server time falls in.</summary>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <returns>The window number.</returns>
    public static long SlotOf(long nowMs) => nowMs / SlotMs;

    /// <summary>Returns the server time of the strike in a window.</summary>
    /// <param name="slot">The window number.</param>
    /// <returns>Server time, Unix milliseconds, inside the window.</returns>
    public static long StrikeAtMs(long slot) => (slot * SlotMs) + (long)(Mix(slot) % (ulong)SlotMs);

    /// <summary>Returns how opaque the white flash is at a moment after a strike.</summary>
    /// <param name="elapsedMs">Milliseconds since the strike; a flash is shown only from 0 to <see cref="FlashMs"/>.</param>
    /// <param name="reduced">Whether motion is reduced: one soft, dim bump instead of the double flicker.</param>
    /// <returns>An alpha from 0 (no flash) to 255.</returns>
    public static int FlashAlpha(long elapsedMs, bool reduced)
    {
        if (elapsedMs < 0 || elapsedMs >= FlashMs)
        {
            return 0;
        }

        if (reduced)
        {
            return Bump(elapsedMs, 0, 80, FlashMs, ReducedPeakAlpha);
        }

        return Math.Max(Bump(elapsedMs, 0, 30, 110, FullPeakAlpha), Bump(elapsedMs, 150, 190, FlashMs, SecondPeakAlpha));
    }

    /// <summary>Returns whether the thunder of a strike is due at this moment: after its delay, for a short window.</summary>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="slot">The window of the strike.</param>
    /// <returns><see langword="true"/> while the thunder should be played.</returns>
    public static bool ThunderDue(long nowMs, long slot)
    {
        long due = StrikeAtMs(slot) + ThunderDelayMs;
        return nowMs >= due && nowMs < due + ThunderWindowMs;
    }

    // A triangle: rises from 0 at `from` to `peak` at `peakAt`, falls back to 0 at `to`.
    private static int Bump(long elapsed, long from, long peakAt, long to, int peak)
    {
        if (elapsed <= from || elapsed >= to)
        {
            return 0;
        }

        return elapsed <= peakAt
            ? (int)((elapsed - from) * peak / (peakAt - from))
            : (int)((to - elapsed) * peak / (to - peakAt));
    }

    // SplitMix64: a well-mixed number from the window's number, the same on every machine.
    private static ulong Mix(long slot)
    {
        ulong z = unchecked((ulong)slot + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
