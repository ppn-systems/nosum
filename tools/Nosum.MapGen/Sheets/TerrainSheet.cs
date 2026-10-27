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

    /// <summary>Returns the pair a transition of <paramref name="upper"/> over <paramref name="lower"/> belongs to, or -1 when the sheet has none.</summary>
    /// <param name="upper">The terrain drawn over.</param>
    /// <param name="lower">The terrain it borders.</param>
    /// <returns>The pair, 0 to 6, or -1.</returns>
    public static int PairOf(Ground upper, Ground lower)
    {
        for (int i = 0; i < s_pairs.Length; i++)
        {
            if (s_pairs[i].Upper == upper && s_pairs[i].Lower == lower)
            {
                return i;
            }
        }

        return -1;
    }

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
                Blend(Area.Of(canvas, Transition(pair, code)), s_pairs[pair].Upper, s_pairs[pair].Lower, pair, code);
            }
        }
    }

    // A transition tile: the mask is the upper terrain where the weight of the corners is above one half. The noise that roughens it repeats
    // every 16 pixels and does not depend on the code, so a transition tile meets its neighbours without a seam.
    private static void Blend(Area area, Ground upper, Ground lower, int pair, int code)
    {
        bool[,] isUpper = new bool[SheetInfo.TileSize, SheetInfo.TileSize];
        int nw = (code & 1) != 0 ? 1 : 0, ne = (code & 2) != 0 ? 1 : 0, sw = (code & 4) != 0 ? 1 : 0, se = (code & 8) != 0 ? 1 : 0;
        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                // The weight of the upper corners at the pixel's centre, in 1/1024.
                int fx = ((2 * x) + 1) * 32, fy = ((2 * y) + 1) * 32;
                int w = (((nw * (1024 - fx)) + (ne * fx)) * (1024 - fy) / 1024) + (((sw * (1024 - fx)) + (se * fx)) * fy / 1024);
                int rough = Shading.Smooth(x, y, 300 + pair, 4) - 128;
                int fine = (int)(Noise.Of(x, y, 400 + pair) % 41) - 20;
                isUpper[x, y] = w + (rough * 4 / 5) + fine > 512;
            }
        }

        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                bool up = isUpper[x, y];
                int distance = DistanceToOther(isUpper, x, y, up);
                area.Put(x, y, Edge(upper, lower, pair, x, y, up, distance));
            }
        }
    }

    // How many pixels (1 to 3, four directions) the nearest pixel of the other side is; 4 when none is that near.
    private static int DistanceToOther(bool[,] mask, int x, int y, bool up)
    {
        for (int d = 1; d <= 3; d++)
        {
            ReadOnlySpan<(int Dx, int Dy)> steps = [(-d, 0), (d, 0), (0, -d), (0, d)];
            foreach ((int dx, int dy) in steps)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx < SheetInfo.TileSize && (uint)ny < SheetInfo.TileSize && mask[nx, ny] != up)
                {
                    return d;
                }
            }
        }

        return 4;
    }

    // The pixel of a transition tile: the fill of its side, with the pair's rim next to the border.
    private static Rgb Edge(Ground upper, Ground lower, int pair, int x, int y, bool up, int distance)
    {
        Rgb own = Pixel(up ? upper : lower, x, y, 0);
        if (distance > 3)
        {
            return own;
        }

        switch (pair)
        {
            case 0: // grass over dirt: a dark blade fringe on the grass, a shadow on the dirt
            case 1: // grass over sand
            case 6: // grass over a cobbled path
                if (up)
                {
                    return distance == 1 ? Shading.Mix(Palette.Leaf.Shade, Palette.GrassRamp[0], 100) : distance == 2 && Noise.Of(x, y, 9) % 3 == 0 ? Palette.GrassRamp[4] : own;
                }

                return distance == 1 ? Shading.Mix(own, Palette.Dirt.Shade, 150) : distance == 2 ? Shading.Mix(own, Palette.Dirt.Shade, 56) : own;
            case 2: // sand over shallow water: wet sand, then foam and a light band in the water
                if (up)
                {
                    return distance == 1 ? Shading.Mix(own, Palette.Dirt.Light, 90) : own;
                }

                return distance == 1 ? Palette.Foam : distance == 2 ? Shading.Mix(own, Palette.Water.Light, 150) : Shading.Mix(own, Palette.Water.Light, 60);
            case 3: // grass over shallow water: a muddy bank, then foam
                if (up)
                {
                    return distance == 1 ? Palette.Dirt.Shade : distance == 2 ? Palette.Leaf.Shade : own;
                }

                return distance == 1 ? Palette.Foam : distance == 2 ? Shading.Mix(own, Palette.Water.Light, 150) : Shading.Mix(own, Palette.Water.Light, 60);
            case 4: // shallow over deep water: the shallows lighten toward the drop, the deep darkens under it
                if (up)
                {
                    return distance == 1 ? Palette.Water.Light : distance == 2 ? Shading.Mix(own, Palette.Water.Light, 110) : own;
                }

                return distance == 1 ? Palette.DeepWater.Shade : distance == 2 ? Shading.Mix(own, Palette.DeepWater.Shade, 110) : own;
            default: // grass over a cliff face: the lit lip, an outline and the face shadowed below it
                if (up)
                {
                    return distance == 1 ? Palette.Outline : distance == 2 ? Palette.GrassRamp[4] : own;
                }

                return distance == 1 ? Palette.Rock.Shade : distance == 2 ? Shading.Mix(own, Palette.Rock.Shade, 140) : Shading.Mix(own, Palette.Rock.Shade, 56);
        }
    }

    private static Rgb Pixel(Ground ground, int x, int y, int variant)
    {
        uint n = Noise.Of(x, y, (variant * 7) + (((int)ground) * 101) + 1);
        return ground switch
        {
            Ground.Grass => Grass(x, y, variant, n),
            Ground.Dirt => Dirt(x, y, variant, n),
            Ground.Sand => Sand(x, y, variant, n),
            Ground.Path => Cobble(x, y, variant),
            Ground.Cliff => Cliff(x, y, variant, n),
            Ground.Shallow => Ripple(Palette.Water, x, y, variant, n, 17),
            _ => Ripple(Palette.DeepWater, x, y, variant, n, 23),
        };
    }

    // Grass: broad light and dim patches from smooth noise, a dithered border between the tones, and blade tufts that differ by variant.
    private static Rgb Grass(int x, int y, int variant, uint n)
    {
        int patch = Shading.Smooth(x, y, 11, 8) + Shading.Smooth(x, y, 12, 4) / 2 + (int)(n % 40) - 20;
        int tone = patch < 120 ? 0 : patch < 190 ? 1 : patch < 250 ? 2 : 3;
        Rgb c = Palette.GrassRamp[tone];
        foreach ((int tx, int ty) in Tufts(variant))
        {
            if (x == tx && y == ty)
            {
                return Palette.Leaf.Shade;
            }

            if ((x == tx && y == ty - 1) || (x == tx - 1 && y == ty - 2) || (x == tx + 1 && y == ty - 2))
            {
                return Palette.GrassRamp[4];
            }
        }

        return c;
    }

    // Where the blade tufts stand: a few per variant, on a staggered lattice so no two neighbours repeat.
    private static (int X, int Y)[] Tufts(int variant) => variant switch
    {
        0 => [(3, 5), (11, 3), (8, 12)],
        1 => [(12, 6), (5, 11), (2, 2)],
        2 => [(7, 4), (13, 13), (3, 9)],
        _ => [(10, 9), (4, 3), (14, 3), (6, 14)],
    };

    // Earth: smooth patches of two tones, a few lit pebbles with a shadow pixel, and flecks.
    private static Rgb Dirt(int x, int y, int variant, uint n)
    {
        int patch = Shading.Smooth(x, y, 21, 8) + (int)(n % 30) - 15;
        Rgb c = patch < 95 ? Shading.Mix(Palette.Dirt.Base, Palette.Dirt.Shade, 110) : patch > 180 ? Shading.Mix(Palette.Dirt.Base, Palette.Dirt.Light, 120) : Palette.Dirt.Base;
        foreach ((int px, int py) in Pebbles(variant))
        {
            if (x == px && y == py)
            {
                return Palette.Dirt.Light;
            }

            if ((x == px + 1 && y == py) || (x == px && y == py + 1))
            {
                return Shading.Mix(Palette.Dirt.Shade, Palette.Outline, 90);
            }
        }

        return n % 29 == 0 ? Palette.Dirt.Shade : c;
    }

    private static (int X, int Y)[] Pebbles(int variant) => variant switch
    {
        0 => [(4, 4), (12, 11)],
        1 => [(9, 3)],
        2 => [(2, 12), (13, 5)],
        _ => [(7, 8)],
    };

    // Sand: fine ripples in slanted rows, light grains and a few dark ones.
    private static Rgb Sand(int x, int y, int variant, uint n)
    {
        int wave = (x + (y * 3) + (variant * 5)) % 16;
        int ripple = Shading.Smooth(x, y, 31, 8);
        if (wave == 0 && ripple > 90)
        {
            return Palette.Sand.Shade;
        }

        if (wave == 1 && ripple > 90)
        {
            return Palette.Sand.Light;
        }

        return n % 23 == 0 ? Palette.Sand.Shade : n % 11 == 0 ? Palette.Sand.Light : Palette.Sand.Base;
    }

    // Cobbles in three courses: rounded stones, each with its own tone, lit on the top left, shaded on the bottom right, moss in the joints.
    private static Rgb Cobble(int x, int y, int variant)
    {
        int course = y < s_courseTop[1] ? 0 : y < s_courseTop[2] ? 1 : 2;
        int row = y - s_courseTop[course];
        int offset = x + (course * 4) + (variant * 3);
        int along = offset % 8;
        int h = s_courseHeight[course];
        bool endRow = row == 1 || row == h - 1;
        bool endCol = along == 1 || along == 7;
        if (row == 0 || along == 0 || (endRow && endCol))
        {
            return Noise.Of(x, y, 61) % 9 == 0 ? Palette.Leaf.Shade : Palette.Mortar;
        }

        uint tone = Noise.Of((offset / 8) + (course * 3), course, 70 + variant) % 4;
        Tones t = tone == 0 ? new Tones(Palette.Cobble.Shade, Palette.Cobble.Light, Palette.White.Base)
            : tone == 1 ? new Tones(Palette.Mortar, Palette.Cobble.Shade, Palette.Cobble.Base) : Palette.Cobble;
        if (row == 1 || along == 1)
        {
            return t.Light;
        }

        if (row == h - 1 || along == 7)
        {
            return t.Shade;
        }

        return Noise.Of(x, y, 62) % 8 == 0 ? t.Light : t.Base;
    }

    // Layered rock: courses of eight rows of blocky stones, offset course by course; each stone is lit on its top, shaded below, with a
    // darker tone in the joints and lumps of tone between.
    private static Rgb Cliff(int x, int y, int variant, uint n)
    {
        int course = y / 8;
        int row = y % 8;
        int along = (x + (course * 5) + (variant * 3)) % 16;
        int stone = along < 7 ? 0 : along < 9 ? -1 : 1;
        if (row == 7)
        {
            return Palette.Rock.Shade;
        }

        if (stone == -1 || along == 0)
        {
            return Shading.Mix(Palette.Rock.Shade, Palette.Rock.Base, 70);
        }

        int lump = Shading.Smooth(x, y, 41, 8) + (int)(n % 40) - 20;
        if (row == 0)
        {
            return Palette.Rock.Light;
        }

        if (row == 1)
        {
            return lump > 130 ? Palette.Rock.Light : Palette.Rock.Base;
        }

        return lump < 90 ? Shading.Mix(Palette.Rock.Base, Palette.Rock.Shade, 110) : lump > 170 ? Shading.Mix(Palette.Rock.Base, Palette.Rock.Light, 80) : Palette.Rock.Base;
    }

    // Water: smooth swells of two tones, slanted lines of glints, and a few bright sparkles that differ by variant.
    private static Rgb Ripple(Tones water, int x, int y, int variant, uint n, int period)
    {
        int swell = Shading.Smooth(x, y, 51, 8, 8) + (int)(n % 20) - 10;
        int line = (x + (2 * y) + (variant * 5)) % period;
        if (line == 3 && swell > 110)
        {
            return water.Light;
        }

        if (line == 4 && swell > 150)
        {
            return Shading.Mix(water.Base, water.Light, 90);
        }

        if (n % 53 == 0 && variant % 2 == 0)
        {
            return Palette.Foam;
        }

        return swell < 100 ? Shading.Mix(water.Base, water.Shade, 70) : swell > 180 ? Shading.Mix(water.Base, water.Light, 45) : water.Base;
    }
}
