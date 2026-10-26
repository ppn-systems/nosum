// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapReauthor;

/// <summary>What a stretch of a placeholder tile is made of, for choosing the own texture that replaces it.</summary>
internal enum Material
{
    /// <summary>Green leaves: tree crowns, bushes, hedges.</summary>
    Leaf,

    /// <summary>Dark conifer green.</summary>
    LeafDark,

    /// <summary>Pink blossom.</summary>
    Blossom,

    /// <summary>Wood and bark.</summary>
    Wood,

    /// <summary>Brown cliff and rock faces.</summary>
    Rock,

    /// <summary>Grey stone.</summary>
    Stone,

    /// <summary>Red roof shingles.</summary>
    RoofRed,

    /// <summary>Slate-blue roof shingles.</summary>
    RoofBlue,

    /// <summary>Light plank or plaster walls and wood floors.</summary>
    Wall,

    /// <summary>Brick and red-brown carpet.</summary>
    Brick,

    /// <summary>Water.</summary>
    Water,

    /// <summary>Sand, straw and hay.</summary>
    Sand,

    /// <summary>Earth.</summary>
    Dirt,

    /// <summary>White: snow, foam, paper, plaster.</summary>
    White,

    /// <summary>Near black: cave darkness.</summary>
    Dark,

    /// <summary>A bright accent: red flowers and fruit.</summary>
    Red,

    /// <summary>A bright accent: yellow flowers.</summary>
    Yellow,

    /// <summary>A bright accent: purple flowers.</summary>
    Purple,
}

/// <summary>The own palette of the drawn objects: the Kenney palette of the phase 2 pictures (<c>tools/art_repack/draw.py</c>) plus the terrain tones.</summary>
internal static class Materials
{
    /// <summary>The outline colour: a dark tone, never black (ADR-040 D1).</summary>
    public static readonly Rgb Outline = new(63, 38, 49);

    // Shadow, base, light tone of each material.
    private static readonly Rgb[][] s_tones =
    [
        [new(38, 92, 66), new(62, 137, 72), new(99, 199, 77)],
        [new(24, 70, 60), new(38, 110, 72), new(70, 165, 80)],
        [new(200, 110, 160), new(240, 160, 200), new(252, 210, 230)],
        [new(119, 56, 51), new(150, 84, 60), new(189, 108, 74)],
        [new(78, 49, 46), new(122, 87, 61), new(165, 135, 85)],
        [new(82, 96, 124), new(140, 156, 181), new(192, 203, 220)],
        [new(156, 48, 52), new(214, 72, 56), new(240, 118, 84)],
        [new(62, 78, 106), new(88, 106, 138), new(122, 140, 170)],
        [new(189, 108, 74), new(222, 150, 98), new(240, 184, 126)],
        [new(112, 50, 50), new(156, 76, 66), new(190, 106, 84)],
        [new(0, 100, 160), new(46, 177, 225), new(118, 228, 255)],
        [new(222, 184, 86), new(255, 224, 120), new(255, 240, 170)],
        [new(122, 96, 58), new(154, 125, 78), new(179, 149, 95)],
        [new(200, 205, 215), new(240, 230, 200), new(255, 255, 250)],
        [new(20, 14, 28), new(30, 22, 40), new(46, 36, 58)],
        [new(160, 40, 44), new(233, 67, 52), new(250, 120, 96)],
        [new(214, 140, 30), new(254, 174, 52), new(255, 224, 120)],
        [new(96, 64, 150), new(140, 96, 200), new(180, 144, 232)],
    ];

    /// <summary>Returns the shadow tone of a material.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The colour.</returns>
    public static Rgb Shade(Material material) => s_tones[(int)material][0];

    /// <summary>Returns the base tone of a material.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The colour.</returns>
    public static Rgb Base(Material material) => s_tones[(int)material][1];

    /// <summary>Returns the light tone of a material.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The colour.</returns>
    public static Rgb Light(Material material) => s_tones[(int)material][2];

    /// <summary>Returns the outline colour of a material: the shared dark tone, darker still for the dark material.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The colour.</returns>
    public static Rgb OutlineOf(Material material) => material == Material.Dark ? new Rgb(12, 8, 18) : Outline;

    /// <summary>Names the material of an average colour: the only thing the painter reads from a placeholder pixel.</summary>
    /// <param name="r">Red, 0 to 255.</param>
    /// <param name="g">Green, 0 to 255.</param>
    /// <param name="b">Blue, 0 to 255.</param>
    /// <returns>The material.</returns>
    public static Material Classify(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double v = max / 255.0, s = max <= 0 ? 0 : (max - min) / max;
        double h = 0;
        if (max > min)
        {
            double d = max - min;
            h = max == r ? ((g - b) / d) % 6.0 : max == g ? ((b - r) / d) + 2.0 : ((r - g) / d) + 4.0;
            h *= 60.0;
            if (h < 0)
            {
                h += 360.0;
            }
        }

        if (v < 0.2)
        {
            return Material.Dark;
        }

        if (s < 0.2)
        {
            return v > 0.85 ? Material.White : Material.Stone;
        }

        if (h is >= 65 and < 175)
        {
            return v < 0.4 ? Material.LeafDark : Material.Leaf;
        }

        if (h is >= 175 and < 265)
        {
            return s > 0.35 && v > 0.62 ? Material.Water : Material.RoofBlue;
        }

        if (h is >= 265 and < 345)
        {
            return v > 0.7 ? Material.Blossom : Material.Purple;
        }

        if (h >= 345 || h < 14)
        {
            if (s > 0.45 && v > 0.5)
            {
                return v < 0.8 ? Material.RoofRed : Material.Red;
            }

            return v > 0.7 ? Material.Blossom : Material.Brick;
        }

        if (h < 46)
        {
            if (s > 0.5)
            {
                return v > 0.82 ? Material.Sand : v < 0.45 ? Material.Wood : Material.Rock;
            }

            return v > 0.7 ? Material.Wall : v < 0.5 ? Material.Wood : Material.Dirt;
        }

        if (h < 65)
        {
            return v > 0.65 ? Material.Sand : Material.Rock;
        }

        return Material.Stone;
    }
}

/// <summary>A colour of three bytes.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
internal readonly record struct Rgb(byte R, byte G, byte B);
