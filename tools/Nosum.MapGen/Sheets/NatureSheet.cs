// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// The nature sheet (docs/adr/ADR-042 P2): trees, bushes, rocks, stumps, logs and fences, drawn in code. A tree is a 2 × 3 block of tiles: its
/// canopy covers the top two rows and its trunk the bottom row. Indices: trees oak 0, maple 2, pine 4 (the block starts there, and its
/// trunk is 32 to 37); 6 to 8 bushes; 9 small rock, 10 and 11 a large rock; 12 stump; 13 log; 14 horizontal fence; 15 vertical fence; 16 to 21 the
/// canopies of the three trees; 22 to 25 fence corners; 26 gate; 27 post; 28 hedge; 29 tall rock; 30 small rock group; 31 sapling.
/// </summary>
internal static class NatureSheet
{
    /// <summary>Tiles the sheet defines.</summary>
    public const int TileCount = 38;

    /// <summary>Draws the sheet.</summary>
    /// <param name="canvas">The sheet's picture.</param>
    public static void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        Tree(Area.Of(canvas, 0, 2, 3), Palette.OakRamp, 0);
        Tree(Area.Of(canvas, 2, 2, 3), Palette.MapleRamp, 1);
        Tree(Area.Of(canvas, 4, 2, 3), Palette.PineRamp, 2);

        Bush(Area.Of(canvas, 6), 0);
        Bush(Area.Of(canvas, 7), 1);
        Bush(Area.Of(canvas, 8), 2);

        Area.Of(canvas, 9).Canopy([(6.5, 11, 3.5), (10.5, 10, 4.5)], Palette.StoneRamp, 9);
        Area.Of(canvas, 10, 2).Canopy([(9, 9, 6), (18, 8, 7), (25, 10, 5.5), (15, 10, 5.5)], Palette.StoneRamp, 10);
        Stump(Area.Of(canvas, 12));
        Log(Area.Of(canvas, 13));

        Straight(Area.Of(canvas, 14), true);
        Straight(Area.Of(canvas, 15), false);
        Corner(Area.Of(canvas, 22), false, true, true, false);
        Corner(Area.Of(canvas, 23), false, false, true, true);
        Corner(Area.Of(canvas, 24), true, true, false, false);
        Corner(Area.Of(canvas, 25), true, false, false, true);
        Gate(Area.Of(canvas, 26));
        Post(Area.Of(canvas, 27));

        Hedge(Area.Of(canvas, 28));
        Area.Of(canvas, 29).Canopy([(6, 10, 5), (10, 9, 5.5), (8, 5.5, 4.5)], Palette.StoneRamp, 29);
        Area.Of(canvas, 30).Canopy([(4, 12, 2.8), (10.5, 11, 3.2), (8, 6.5, 2.6)], Palette.StoneRamp, 30);
        Sapling(Area.Of(canvas, 31));
    }

    // A tree over the 2 × 3 block: the ground shadow, the trunk with its roots, then the canopy over its top.
    private static void Tree(Area area, Rgb[] ramp, int species)
    {
        // A checkered shadow on the grass round the foot of the trunk.
        for (int y = 41; y < 48; y++)
        {
            for (int x = 3; x < 29; x++)
            {
                double dx = (x - 16) / 13.0, dy = (y - 45) / 3.6;
                if (((x + y) & 1) == 0 && (dx * dx) + (dy * dy) <= 1)
                {
                    area.Put(x, y, Palette.Shadow);
                }
            }
        }

        // The trunk: bark in strips, lit on the left, with the roots flaring over the last rows.
        for (int y = 27; y < 48; y++)
        {
            int flare = y >= 44 ? y - 43 : 0;
            int left = 13 - flare, right = 18 + flare;
            for (int x = left; x <= right; x++)
            {
                Rgb c = x == left ? Palette.Outline : x == right ? Palette.Outline : x == left + 1 ? Palette.Wood.Light : x >= right - 1 ? Palette.Wood.Shade
                    : Noise.Of(x, y / 3, 80) % 3 == 0 ? Palette.Wood.Shade : Palette.Wood.Base;
                area.Put(x, y, c);
            }
        }

        area.Fill(12, 47, 9, 1, Palette.Outline);
        if (species == 2)
        {
            Pine(area, ramp);
            return;
        }

        (double, double, double)[] lobes = species == 1
            ? [(16, 8, 8.5), (8, 13, 7.5), (24, 13, 7.5), (16, 15, 10.5), (9, 21, 6.5), (23, 21, 6.5), (16, 23, 7)]
            : [(16, 8, 8), (9, 13, 8), (23, 13, 8), (16, 15, 10), (10, 21, 7), (22, 21, 7), (16, 23, 6.5)];
        area.Canopy(lobes, ramp, 90 + species);
    }

    // A pine: four tiers of needles, each wider than the one above it, lit on the left, with a scalloped lower edge.
    private static void Pine(Area area, Rgb[] ramp)
    {
        (int Top, int Height, int Half)[] tiers = [(1, 10, 6), (7, 11, 9), (14, 12, 12), (21, 10, 14)];
        int[,] owner = new int[32, 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                owner[x, y] = -1;
            }
        }

        for (int t = 0; t < tiers.Length; t++)
        {
            for (int y = tiers[t].Top; y < tiers[t].Top + tiers[t].Height && y < 31; y++)
            {
                int along = y - tiers[t].Top;
                int half = 1 + (along * tiers[t].Half / tiers[t].Height);
                // A ragged lower edge: the last two rows are cut in a zigzag.
                if (along >= tiers[t].Height - 2)
                {
                    half -= ((y / 2) % 2 == 0) ? 0 : 1;
                }

                for (int x = 16 - half; x < 16 + half; x++)
                {
                    owner[x, y] = t;
                }
            }
        }

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int o = owner[x, y];
                if (o < 0)
                {
                    continue;
                }

                if (Open(owner, x - 1, y) || Open(owner, x + 1, y) || Open(owner, x, y - 1) || Open(owner, x, y + 1))
                {
                    area.Put(x, y, Palette.Outline);
                    continue;
                }

                int along = y - tiers[o].Top;
                bool underNext = o + 1 < tiers.Length && (Later(owner, x, y + 1, o) || Later(owner, x, y + 2, o));
                int tone = x < 14 ? 3 : x < 17 ? 2 : 1;
                if (along < 2)
                {
                    tone++;
                }

                if (along >= tiers[o].Height - 3 && !underNext)
                {
                    tone--;
                }

                if (underNext)
                {
                    tone = 0;
                }

                uint n = Noise.Of(x, y, 95);
                if (n % 6 == 0)
                {
                    tone += n % 12 == 0 ? 1 : -1;
                }

                area.Put(x, y, ramp[Math.Clamp(tone, 0, 4)]);
            }
        }
    }

    private static bool Open(int[,] owner, int x, int y) => (uint)x >= 32u || (uint)y >= 32u || owner[x, y] < 0;

    private static bool Later(int[,] owner, int x, int y, int tier) => (uint)x < 32u && (uint)y < 32u && owner[x, y] > tier;

    // A bush: a lumpy mass of leaves; kind 1 carries red berries, kind 2 pink blossom.
    private static void Bush(Area area, int kind)
    {
        // Its shadow first, then the leaves.
        for (int x = 3; x < 14; x++)
        {
            if ((x & 1) == 0)
            {
                area.Put(x, 14, Palette.Shadow);
            }
        }

        area.Canopy([(5.5, 9.5, 4.8), (10.5, 9.5, 4.8), (8, 7.5, 5), (8, 10.5, 5)], Palette.OakRamp, 120 + kind);
        if (kind == 1)
        {
            area.Orb(5, 8, 1.2, Palette.Red);
            area.Orb(11, 10, 1.2, Palette.Red);
            area.Orb(8, 6, 1.2, Palette.Red);
            area.Orb(7, 11, 1.2, Palette.Red);
        }
        else if (kind == 2)
        {
            foreach ((int x, int y) in new[] { (5, 8), (11, 10), (8, 6), (7, 11), (11, 6) })
            {
                area.Put(x, y, Palette.Blossom.Base);
                area.Put(x, y - 1, Palette.Blossom.Light);
                area.Put(x - 1, y, Palette.Blossom.Light);
                area.Put(x + 1, y, Palette.Blossom.Shade);
            }
        }
    }

    // A stump: a barrel of bark under a cut top with growth rings.
    private static void Stump(Area area)
    {
        for (int y = 8; y < 15; y++)
        {
            int half = y >= 13 ? 7 : 6;
            for (int x = 8 - half; x < 8 + half; x++)
            {
                Rgb c = x == 8 - half || x == 7 + half ? Palette.Outline : x < 5 ? Palette.Wood.Light : x > 10 ? Palette.Wood.Shade
                    : Noise.Of(x, 0, 100) % 3 == 0 ? Palette.Wood.Shade : Palette.Wood.Base;
                area.Put(x, y, y == 14 ? Palette.Outline : c);
            }
        }

        area.Blob(8, 7.5, 6, 3.4, Palette.Wood);
        for (int x = 4; x < 13; x++)
        {
            for (int y = 5; y < 11; y++)
            {
                double dx = (x - 8) / 4.2, dy = (y - 7.5) / 2.1;
                double d = (dx * dx) + (dy * dy);
                if (d < 1 && (d > 0.78 || (d > 0.3 && d < 0.4)))
                {
                    area.Put(x, y, Palette.Wood.Shade);
                }
                else if (d < 1)
                {
                    area.Put(x, y, d < 0.3 ? Palette.Sand.Shade : Palette.Sand.Base);
                }
            }
        }
    }

    // A log on its side: bark along its length, the cut end with rings on the left.
    private static void Log(Area area)
    {
        area.Wood(3, 5, 12, 8, true, 110);
        area.Blob(3.5, 8.5, 3, 4, Palette.Wood);
        area.Put(3, 8, Palette.Wood.Shade);
        area.Put(4, 8, Palette.Wood.Shade);
        area.Put(3, 9, Palette.Sand.Base);
        area.Put(4, 9, Palette.Sand.Base);
        area.Put(3, 7, Palette.Sand.Light);
        for (int x = 3; x < 15; x++)
        {
            if ((x & 1) == 0)
            {
                area.Put(x, 14, Palette.Shadow);
            }
        }
    }

    // A rail: wood across or down with a lit face; only its long sides are outlined, so a run of tiles joins without a break.
    private static void Rail(Area area, int x, int y, int length, bool horizontal, int seed)
    {
        if (horizontal)
        {
            area.Wood(x, y, length, 3, true, seed, false);
            area.Fill(x, y - 1, length, 1, Palette.Outline);
            area.Fill(x, y + 3, length, 1, Palette.Outline);
            area.Fill(x, y + 4, length, 1, Palette.Shadow);
        }
        else
        {
            area.Wood(x, y, 3, length, false, seed, false);
            area.Fill(x - 1, y, 1, length, Palette.Outline);
            area.Fill(x + 3, y, 1, length, Palette.Outline);
            area.Fill(x + 4, y, 1, length, Palette.Shadow);
        }
    }

    // A fence post, three wide, with a rounded cap lit on the left.
    private static void FencePost(Area area, int x, int y, int height)
    {
        area.Wood(x, y, 3, height, false, 130 + x);
        area.Put(x, y, Palette.Outline);
        area.Put(x + 2, y, Palette.Outline);
        area.Put(x + 1, y + 1, Palette.Wood.Light);
        area.Fill(x, y + height, 3, 1, Palette.Shadow);
    }

    // A straight run: two horizontal rails across the tile between posts, or a continuous vertical rail with a wider post.
    private static void Straight(Area area, bool horizontal)
    {
        if (horizontal)
        {
            Rail(area, 0, 4, 16, true, 140);
            Rail(area, 0, 9, 16, true, 141);
            FencePost(area, 1, 2, 12);
            FencePost(area, 12, 2, 12);
            return;
        }

        Rail(area, 6, 0, 16, false, 142);
        CenterPost(area, north: true, south: true);
    }

    // A corner post at the middle with rails to the sides named.
    private static void Corner(Area area, bool north, bool east, bool south, bool west)
    {
        if (north)
        {
            Rail(area, 6, 0, 9, false, 144);
        }

        if (south)
        {
            Rail(area, 6, 7, 9, false, 145);
        }

        if (east)
        {
            Rail(area, 8, 4, 8, true, 146);
            Rail(area, 8, 9, 8, true, 152);
        }

        if (west)
        {
            Rail(area, 0, 4, 8, true, 147);
            Rail(area, 0, 9, 8, true, 153);
        }

        CenterPost(area, north, south);
    }

    // A 5px-wide post centered on x = 5..9 (over the 3px vertical rail at x = 6..8) that keeps vertical rails continuous.
    private static void CenterPost(Area area, bool north, bool south)
    {
        area.Wood(5, 2, 5, 11, false, 136);
        area.Fill(5, 2, 5, 1, Palette.Outline);
        area.Fill(6, 3, 3, 2, Palette.Wood.Light);
        area.Fill(5, 13, 5, 1, Palette.Outline);
        if (!south)
        {
            area.Fill(5, 14, 5, 1, Palette.Shadow);
        }
        else
        {
            // Keep the vertical rail flowing cleanly into the south neighbour without a black cut across x = 6..8.
            area.Put(6, 13, Palette.Wood.Light);
            area.Put(7, 13, Palette.Wood.Base);
            area.Put(8, 13, Palette.Wood.Shade);
        }

        if (north)
        {
            area.Put(6, 2, Palette.Wood.Light);
            area.Put(7, 2, Palette.Wood.Base);
            area.Put(8, 2, Palette.Wood.Shade);
        }
    }

    private static void Gate(Area area)
    {
        Rail(area, 6, 0, 3, false, 154);
        Rail(area, 6, 13, 3, false, 155);
        FencePost(area, 1, 2, 12);
        FencePost(area, 12, 2, 12);
        Rail(area, 0, 4, 4, true, 148);
        Rail(area, 12, 4, 4, true, 149);
        Rail(area, 0, 9, 4, true, 150);
        Rail(area, 12, 9, 4, true, 151);
    }

    private static void Post(Area area)
    {
        FencePost(area, 6, 2, 12);
        area.Put(7, 3, Palette.Wood.Light);
    }

    // A hedge: a row of clipped leafy lobes over a solid body; its sides run on past the tile, so neighbours join.
    private static void Hedge(Area area)
    {
        area.Canopy([(-1, 8, 4.8), (3.5, 6.5, 5), (8.5, 6, 5.2), (13, 6.5, 5), (17, 8, 4.8), (2, 11, 5), (8, 11.5, 5.5), (14, 11, 5), (-2, 12, 4), (18, 12, 4)], Palette.OakRamp, 160, true);
        area.Fill(0, 15, 16, 1, Palette.Outline);
    }

    // A sapling: a thin trunk and a small crown.
    private static void Sapling(Area area)
    {
        area.Fill(7, 8, 2, 7, Palette.Wood.Base);
        area.Fill(7, 8, 1, 7, Palette.Wood.Light);
        area.Fill(8, 14, 1, 1, Palette.Outline);
        area.Canopy([(8, 5.5, 3.5), (5.5, 7, 3), (10.5, 7, 3)], Palette.OakRamp, 170);
    }
}
