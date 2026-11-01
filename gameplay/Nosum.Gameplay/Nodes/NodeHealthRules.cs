// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Nodes;

/// <summary>How much of a node's health is left, for the bar drawn over it (docs/design/critique-gathering.md, finding G1).</summary>
public enum NodeHealthLevel
{
    /// <summary>More than half the hits are left.</summary>
    Healthy = 0,

    /// <summary>Between a quarter and a half of the hits are left.</summary>
    Hurt = 1,

    /// <summary>A quarter or fewer of the hits are left.</summary>
    Badly = 2,
}

/// <summary>
/// The rule of the node hit-points bar: a node shows it only once it has taken a hit and still stands, so a full node carries no mark and a
/// one-hit node (stone, twig, weed) never shows one. Pure; the client draws the pixels.
/// </summary>
public static class NodeHealthRules
{
    /// <summary>Returns whether a node shows the bar: it takes more than one hit in total, and it stands with some hits taken.</summary>
    /// <param name="hits">Hits left.</param>
    /// <param name="maxHits">Hits the node takes in total.</param>
    /// <returns><see langword="true"/> for a node that is damaged but still standing.</returns>
    public static bool Shows(int hits, int maxHits) => maxHits > 1 && hits > 0 && hits < maxHits;

    /// <summary>Returns the filled part of a bar of <paramref name="width"/> pixels: whole pixels, never zero for a standing node.</summary>
    /// <param name="hits">Hits left.</param>
    /// <param name="maxHits">Hits the node takes in total.</param>
    /// <param name="width">The bar's width in pixels.</param>
    /// <returns>A width from one to <paramref name="width"/>.</returns>
    public static int FillPixels(int hits, int maxHits, int width)
    {
        if (maxHits <= 0)
        {
            return width;
        }

        int pixels = (int)Math.Ceiling((double)width * Math.Clamp(hits, 0, maxHits) / maxHits);
        return Math.Clamp(pixels, 1, width);
    }

    /// <summary>Returns how healthy a node looks: above half is healthy, above a quarter is hurt, otherwise badly hurt.</summary>
    /// <param name="hits">Hits left.</param>
    /// <param name="maxHits">Hits the node takes in total.</param>
    /// <returns>The level.</returns>
    public static NodeHealthLevel LevelOf(int hits, int maxHits)
    {
        if (hits * 2 > maxHits)
        {
            return NodeHealthLevel.Healthy;
        }

        return hits * 4 > maxHits ? NodeHealthLevel.Hurt : NodeHealthLevel.Badly;
    }
}
