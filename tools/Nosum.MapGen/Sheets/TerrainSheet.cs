// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// The outdoor terrain sheet (docs/adr/ADR-042 P2), drawn in code. The fills come first, four texture variants of each kind: index
/// <c>kind × 4 + variant</c> for grass 0, dirt 1, sand 2, path 3, cliff 4, shallow water 5 and deep water 6. Then 14 transition tiles for each
/// of seven pairs (the upper terrain over the lower), indexed <c>28 + 14 × pair + (code − 1)</c>, where the code's bits are the upper corners
/// (1 north-west, 2 north-east, 4 south-west, 8 south-east). The transitions carry no traits: the fills under them do.
/// </summary>
internal static class TerrainSheet
{
    /// <summary>Tiles the sheet defines.</summary>
    public const int TileCount = 126;

    /// <summary>Texture variants of every fill.</summary>
    public const int Variants = 4;

    /// <summary>The first transition tile.</summary>
    public const int FirstTransition = 28;

    /// <summary>The kinds of fill, in index order.</summary>
    public enum Ground
    {
        /// <summary>Grass.</summary>
        Grass,

        /// <summary>Earth you may till.</summary>
        Dirt,

        /// <summary>Sand.</summary>
        Sand,

        /// <summary>Cobbled path.</summary>
        Path,

        /// <summary>Cliff face.</summary>
        Cliff,

        /// <summary>Shallow water.</summary>
        Shallow,

        /// <summary>Deep water.</summary>
        Deep,
    }

    // The seven transition pairs: the upper terrain and the lower one.
    private static readonly (Ground Upper, Ground Lower)[] s_pairs =
    [
        (Ground.Grass, Ground.Dirt),
        (Ground.Grass, Ground.Sand),
        (Ground.Sand, Ground.Shallow),
        (Ground.Grass, Ground.Shallow),
        (Ground.Shallow, Ground.Deep),
        (Ground.Grass, Ground.Cliff),
        (Ground.Grass, Ground.Path),
    ];

    // Where each cobble course starts and how tall it is.
    private static readonly int[] s_courseTop = [0, 5, 11];
    private static readonly int[] s_courseHeight = [5, 6, 5];

    /// <summary>Returns the index of a fill.</summary>
    /// <param name="ground">The kind of fill.</param>
    /// <param name="variant">The texture variant, 0 to <see cref="Variants"/> − 1.</param>
    /// <returns>The tile index.</returns>
    public static int Fill(Ground ground, int variant) => ((int)ground * Variants) + variant;

    /// <summary>Returns the index of a transition tile.</summary>
    /// <param name="pair">The pair, 0 to 6, in the order of the pairs of this sheet.</param>
    /// <param name="code">The corner code, 1 to 14.</param>
    /// <returns>The tile index.</returns>
    public static int Transition(int pair, int code) => FirstTransition + (14 * pair) + (code - 1);

    /// <summary>Draws the sheet.</summary>
    /// <param name="canvas">The sheet's picture.</param>
    public static void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        for (int kind = 0; kind <= (int)Ground.Deep; kind++)
        {
            for (int variant = 0; variant < Variants; variant++)
            {
                Area area = Area.Of(canvas, Fill((Ground)kind, variant));
                for (int y = 0; y < SheetInfo.TileSize; y++)
                {
                    for (int x = 0; x < SheetInfo.TileSize; x++)
                    {
                        area.Put(x, y, Pixel((Ground)kind, x, y, variant));
                    }
                }
            }
        }

        for (int pair = 0; pair < s_pairs.Length; pair++)
        {
            for (int code = 1; code < 15; code++)
            {
                Blend(Area.Of(canvas, Transition(pair, code)), s_pairs[pair].Upper, s_pairs[pair].Lower, code);
            }
        }
    }

    // A transition tile: the upper terrain where the corner weight, roughened by noise, is above one half, the lower terrain elsewhere.
    private static void Blend(Area area, Ground upper, Ground lower, int code)
    {
        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                // The weight of the upper corners at the pixel's centre, in 1/1024.
                int fx = ((2 * x) + 1) * 32, fy = ((2 * y) + 1) * 32;
                int nw = (code & 1) != 0 ? 1 : 0, ne = (code & 2) != 0 ? 1 : 0, sw = (code & 4) != 0 ? 1 : 0, se = (code & 8) != 0 ? 1 : 0;
                int w = (((nw * (1024 - fx)) + (ne * fx)) * (1024 - fy) / 1024) + (((sw * (1024 - fx)) + (se * fx)) * fy / 1024);
                int noise = (int)(Noise.Of(x, y, 200 + code) % 129) - 64;
                area.Put(x, y, w + noise > 512 ? Pixel(upper, x, y, 0) : Pixel(lower, x, y, 0));
            }
        }
    }

    private static Rgb Pixel(Ground ground, int x, int y, int variant)
    {
        uint n = Noise.Of(x, y, (variant * 7) + (((int)ground) * 101) + 1);
        return ground switch
        {
            Ground.Grass => n % 9 == 0 ? Palette.Leaf.Shade : n % 17 == 0 ? Palette.Leaf.Light : Palette.Leaf.Base,
            Ground.Dirt => n % 6 == 0 ? Palette.Dirt.Shade : n % 19 == 0 ? Palette.Dirt.Light : Palette.Dirt.Base,
            Ground.Sand => n % 7 == 0 ? Palette.Sand.Shade : n % 21 == 0 ? Palette.Sand.Light : Palette.Sand.Base,
            Ground.Path => Cobble(x, y, variant),
            Ground.Cliff => Cliff(x, y, n),
            Ground.Shallow => Ripple(Palette.Water, x, y, n),
            _ => Ripple(Palette.DeepWater, x, y, n),
        };
    }

    // Cobbles in three courses; each stone has its own tone, and its top and left edges catch the light.
    private static Rgb Cobble(int x, int y, int variant)
    {
        int course = y < s_courseTop[1] ? 0 : y < s_courseTop[2] ? 1 : 2;
        int row = y - s_courseTop[course];
        int offset = x + (course * 4) + (variant * 3);
        int along = offset % 8;
        if (row == 0 || along == 0)
        {
            return Palette.Mortar;
        }

        if (row == 1 || along == 1)
        {
            return Palette.Cobble.Light;
        }

        if (row == s_courseHeight[course] - 1 || along == 7)
        {
            return Palette.Cobble.Shade;
        }

        uint tone = Noise.Of(offset / 8, course, variant) % 3;
        return tone == 0 ? Palette.Cobble.Light : tone == 1 ? Palette.Cobble.Shade : Palette.Cobble.Base;
    }

    // Layered rock: a dark stratum every fifth row, a broken light crack, and grain between.
    private static Rgb Cliff(int x, int y, uint n)
    {
        if (y % 5 == 0)
        {
            return Palette.Rock.Shade;
        }

        if (((x + ((y / 5) * 7)) % 11) == 0)
        {
            return Palette.Rock.Light;
        }

        return n % 5 == 0 ? Palette.Rock.Shade : Palette.Rock.Base;
    }

    // Water with a ripple line every eleven pixels on a slant, and a few dim specks.
    private static Rgb Ripple(Tones water, int x, int y, uint n)
    {
        if (((x + (2 * y)) % 11) == 3)
        {
            return water.Light;
        }

        return n % 31 == 0 ? water.Shade : water.Base;
    }
}
