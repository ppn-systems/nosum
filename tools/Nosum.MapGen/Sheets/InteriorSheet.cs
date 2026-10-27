// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// The interior sheet (docs/adr/ADR-042 P2), drawn in code: wooden and straw floors (0 to 9, on the Back layer), walls and the door (10 to 15),
/// and the furniture of the house, the coop and the barn (16 to 40). The farmhouse bed is a 2 × 2 block (16, 17 over 32, 33), the table another
/// (18, 19 over 34, 35). Indices are listed in <c>interior.tileset</c>, which holds the traits.
/// </summary>
internal static class InteriorSheet
{
    /// <summary>Tiles the sheet defines.</summary>
    public const int TileCount = 41;

    /// <summary>Draws the sheet.</summary>
    /// <param name="canvas">The sheet's picture.</param>
    public static void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        for (int i = 0; i < 4; i++)
        {
            Planks(Area.Of(canvas, i), i);
        }

        Rug(Area.Of(canvas, 4));
        Straw(Area.Of(canvas, 5), 1);
        Straw(Area.Of(canvas, 6), 2);
        Planks(Area.Of(canvas, 7), 4, dark: true);
        Straw(Area.Of(canvas, 8), 3, hay: true);
        Run(Area.Of(canvas, 9));

        Plaster(Area.Of(canvas, 10));
        Wainscot(Area.Of(canvas, 11));
        Cap(Area.Of(canvas, 12));
        Corner(Area.Of(canvas, 13));
        Window(Area.Of(canvas, 14));
        Door(Area.Of(canvas, 15));

        Bed(Area.Of(canvas, 16, 2, 2));
        Table(Area.Of(canvas, 18, 2, 2));
        Stove(Area.Of(canvas, 20));
        Sink(Area.Of(canvas, 21));
        Counter(Area.Of(canvas, 22));
        Cupboard(Area.Of(canvas, 23));
        Shelf(Area.Of(canvas, 24));
        Trough(Area.Of(canvas, 25), true);
        Trough(Area.Of(canvas, 26), false);
        Bale(Area.Of(canvas, 27));
        Bench(Area.Of(canvas, 28, 2, 1));
        Chair(Area.Of(canvas, 30));
        Nest(Area.Of(canvas, 31), false);

        Nest(Area.Of(canvas, 36), true);
        Perch(Area.Of(canvas, 37));
        Gate(Area.Of(canvas, 38));
        Stack(Area.Of(canvas, 39));
        Bench(Area.Of(canvas, 40));
    }

    // Floor boards in courses of four rows: each board has its own tone, a lit top edge, a dark joint under it and nails, and the end
    // seams do not line up from one course to the next.
    private static void Planks(Area area, int variant, bool dark = false)
    {
        Tones t = dark ? new Tones(Palette.Dark.Base, new Rgb(72, 48, 58), new Rgb(96, 66, 74)) : Palette.Wood;
        for (int course = 0; course < 4; course++)
        {
            int y = course * 4;
            int seam = ((course * 7) + (variant * 5)) % 16;
            for (int x = 0; x < 16; x++)
            {
                int board = x >= seam ? 1 : 0;
                uint tone = Noise.Of(course + (variant * 4), board, 140) % 3;
                Rgb c = tone == 0 ? Shading.Mix(t.Base, t.Light, 90) : tone == 1 ? t.Base : Shading.Mix(t.Base, t.Shade, 60);
                area.Fill(x, y, 1, 4, c);
                area.Put(x, y, Shading.Mix(c, t.Light, 110));
                area.Put(x, y + 3, Shading.Mix(c, t.Shade, 170));
                if (Noise.Of(x, y / 2, 141 + variant) % 13 == 0)
                {
                    area.Put(x, y + 1, Shading.Mix(c, t.Shade, 90));
                }
            }

            area.Fill(seam, y, 1, 4, t.Shade);
            area.Put((seam + 2) % 16, y + 1, t.Shade);
            area.Put((seam + 14) % 16, y + 1, t.Shade);
        }
    }

    // A woven rug: a deep red field with a thin yellow border and a stitched line inside it. The tile repeats across a room, so it stays calm.
    private static void Rug(Area area)
    {
        area.Fill(0, 0, 16, 16, Shading.Mix(Palette.Red.Base, Palette.Red.Shade, 90));
        area.Speckle(0, 0, 16, 16, Palette.Red.Shade, 150, 6);
        area.Speckle(0, 0, 16, 16, Palette.Red.Base, 151, 7);
        area.Frame(0, 0, 16, 16, Palette.Red.Shade);
        area.Frame(1, 1, 14, 14, Shading.Mix(Palette.Yellow.Base, Palette.Red.Shade, 90));
        for (int x = 3; x < 13; x += 2)
        {
            area.Put(x, 3, Shading.Mix(Palette.Yellow.Base, Palette.Red.Base, 110));
            area.Put(x, 12, Shading.Mix(Palette.Yellow.Base, Palette.Red.Base, 110));
        }
    }

    // Straw on packed earth. Hay makes the straw denser and warmer.
    private static void Straw(Area area, int seed, bool hay = false)
    {
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int patch = Shading.Smooth(x, y, 52, 8) + (int)(Noise.Of(x, y, 53) % 40) - 20;
                area.Put(x, y, patch < 100 ? Palette.Dirt.Shade : patch > 190 ? Palette.Dirt.Light : Palette.Dirt.Base);
            }
        }

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                uint n = Noise.Of(x, y, 50 + seed);
                int density = hay ? 3 : 7;
                if (((x + (2 * y) + seed) % 9 < 2) && n % density != 0)
                {
                    area.Put(x, y, n % 3 == 0 ? Palette.Sand.Shade : n % 2 == 0 ? Palette.Sand.Base : Palette.Sand.Light);
                }
            }
        }
    }

    private static void Run(Area area)
    {
        Straw(area, 9);
        area.Canopy([(4, 11, 2.5), (11, 5.5, 2.8)], Palette.StoneRamp, 160);
    }

    // The wall's render: a warm tone with a darker skirting and a lit cornice.
    private static void Plaster(Area area)
    {
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int mottle = Shading.Smooth(x, y, 6, 8) + (int)(Noise.Of(x, y, 7) % 40) - 20;
                area.Put(x, y, mottle < 100 ? Shading.Mix(Palette.Plaster.Base, Palette.Plaster.Shade, 80) : mottle > 190 ? Shading.Mix(Palette.Plaster.Base, Palette.Plaster.Light, 70) : Palette.Plaster.Base);
            }
        }

        area.Fill(0, 12, 16, 4, Palette.Plaster.Shade);
        area.Fill(0, 12, 16, 1, Shading.Mix(Palette.Plaster.Shade, Palette.Plaster.Base, 100));
        area.Fill(0, 15, 16, 1, Shading.Mix(Palette.Plaster.Shade, Palette.Outline, 110));
        area.Fill(0, 0, 16, 1, Palette.Plaster.Light);
    }

    private static void Wainscot(Area area)
    {
        area.Wood(0, 0, 16, 16, false, 160, false);
        area.Frame(1, 3, 6, 9, Palette.Wood.Shade);
        area.Frame(9, 3, 6, 9, Palette.Wood.Shade);
        area.Fill(2, 4, 4, 1, Palette.Wood.Light);
        area.Fill(10, 4, 4, 1, Palette.Wood.Light);
        area.Fill(0, 0, 16, 2, Palette.Wood.Light);
        area.Fill(0, 2, 16, 1, Palette.Wood.Shade);
        area.Fill(0, 15, 16, 1, Palette.Outline);
    }

    private static void Cap(Area area)
    {
        area.Bevel(0, 0, 16, 16, Palette.Stone);
        area.Speckle(1, 1, 14, 14, Palette.Stone.Light, 161, 7);
        area.Speckle(1, 1, 14, 14, Palette.Stone.Shade, 162, 9);
        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    private static void Corner(Area area)
    {
        Plaster(area);
        area.Wood(0, 0, 3, 16, false, 163, false);
        area.Wood(13, 0, 3, 16, false, 164, false);
    }

    private static void Window(Area area)
    {
        Plaster(area);
        area.Frame(2, 2, 12, 12, Palette.Outline);
        area.Fill(3, 3, 10, 10, Palette.Wood.Base);
        for (int y = 4; y < 12; y++)
        {
            for (int x = 4; x < 12; x++)
            {
                area.Put(x, y, Shading.Mix(Palette.Water.Light, Palette.Water.Base, (y - 4) * 24));
            }
        }

        area.Fill(7, 4, 2, 8, Palette.Wood.Base);
        area.Fill(4, 7, 8, 2, Palette.Wood.Base);
        area.Fill(7, 4, 1, 8, Palette.Wood.Light);
        area.Fill(4, 7, 8, 1, Palette.Wood.Light);
        area.Put(5, 5, Palette.White.Light);
        area.Put(5, 6, Palette.White.Light);
        area.Put(9, 10, Palette.White.Light);
    }

    private static void Door(Area area)
    {
        area.Wood(0, 0, 16, 16, false, 165);
        area.Bevel(2, 2, 12, 5, new Tones(Palette.Wood.Light, Palette.Wood.Shade, Palette.Wood.Base));
        area.Bevel(2, 9, 12, 5, new Tones(Palette.Wood.Light, Palette.Wood.Shade, Palette.Wood.Base));
        area.Orb(11.5, 8, 1.3, Palette.Yellow);
    }

    // A bed of 32 × 32 px: the headboard and pillows in the top half, a red quilt with a check pattern, a folded sheet and wooden posts.
    private static void Bed(Area area)
    {
        area.Wood(0, 0, 32, 9, true, 170);
        area.Fill(1, 1, 30, 1, Palette.Wood.Light);
        area.Frame(0, 0, 32, 32, Palette.Outline);
        area.Fill(2, 4, 12, 7, Palette.White.Base);
        area.Fill(2, 4, 12, 1, Palette.White.Light);
        area.Fill(2, 10, 12, 1, Palette.White.Shade);
        area.Fill(17, 4, 13, 7, Palette.White.Base);
        area.Fill(17, 4, 13, 1, Palette.White.Light);
        area.Fill(17, 10, 13, 1, Palette.White.Shade);
        area.Fill(1, 11, 30, 20, Palette.Red.Base);
        for (int y = 14; y < 31; y++)
        {
            for (int x = 1; x < 31; x++)
            {
                if (((x / 4) + (y / 4)) % 2 == 0)
                {
                    area.Put(x, y, Shading.Mix(Palette.Red.Base, Palette.Red.Light, 70));
                }
            }
        }

        area.Fill(1, 11, 30, 3, Palette.White.Base);
        area.Fill(1, 11, 30, 1, Palette.White.Light);
        area.Fill(1, 13, 30, 1, Palette.White.Shade);
        area.Fill(1, 29, 30, 2, Palette.Red.Shade);
    }

    // A table of 32 × 32 px: a top with a lit edge and a front face over four turned legs.
    private static void Table(Area area)
    {
        area.Wood(0, 2, 32, 10, true, 171);
        area.Fill(1, 3, 30, 1, Palette.Wood.Light);
        area.Wood(1, 12, 30, 3, true, 172, false);
        area.Wood(2, 15, 4, 16, false, 173);
        area.Wood(26, 15, 4, 16, false, 174);
        area.Fill(6, 29, 20, 1, Palette.Shadow);
        area.Orb(14, 7, 2, Palette.Yellow);
    }

    private static void Stove(Area area)
    {
        area.Bevel(0, 0, 16, 16, Palette.Stone);
        area.Frame(0, 0, 16, 16, Palette.Outline);
        area.Fill(2, 2, 12, 3, Palette.Dark.Base);
        area.Fill(3, 3, 3, 1, Palette.Stone.Shade);
        area.Fill(9, 3, 3, 1, Palette.Stone.Shade);
        area.Fill(3, 7, 10, 7, Palette.Dark.Base);
        area.Frame(3, 7, 10, 7, Palette.Outline);
        area.Fill(5, 10, 6, 3, Palette.Yellow.Base);
        area.Fill(6, 10, 4, 2, Palette.Yellow.Light);
        area.Put(8, 9, Palette.Red.Light);
    }

    private static void Sink(Area area)
    {
        Counter(area);
        area.Wood(3, 2, 10, 5, true, 175, false);
        area.Frame(3, 2, 10, 5, Palette.Outline);
        area.Fill(4, 3, 8, 3, Palette.Water.Base);
        area.Fill(4, 3, 8, 1, Palette.Water.Light);
        area.Fill(7, 0, 2, 3, Palette.Stone.Base);
    }

    private static void Counter(Area area)
    {
        area.Wood(0, 6, 16, 10, true, 176);
        area.Wood(0, 0, 16, 7, true, 177);
        area.Fill(0, 1, 16, 2, Palette.Wood.Light);
        area.Fill(7, 8, 1, 7, Palette.Wood.Shade);
        area.Fill(8, 8, 1, 7, Palette.Wood.Light);
        area.Fill(3, 10, 2, 1, Palette.Yellow.Base);
        area.Fill(11, 10, 2, 1, Palette.Yellow.Base);
    }

    private static void Cupboard(Area area)
    {
        area.Wood(0, 0, 16, 16, false, 178);
        area.Bevel(2, 2, 6, 12, new Tones(Palette.Wood.Light, Palette.Wood.Shade, Palette.Wood.Base));
        area.Bevel(8, 2, 6, 12, new Tones(Palette.Wood.Light, Palette.Wood.Shade, Palette.Wood.Base));
        area.Put(6, 7, Palette.Yellow.Base);
        area.Put(6, 8, Palette.Yellow.Base);
        area.Put(9, 7, Palette.Yellow.Base);
        area.Put(9, 8, Palette.Yellow.Base);
    }

    private static void Shelf(Area area)
    {
        area.Wood(0, 0, 16, 16, false, 179);
        area.Fill(1, 1, 14, 14, Palette.Wood.Shade);
        area.Fill(1, 5, 14, 2, Palette.Wood.Base);
        area.Fill(1, 5, 14, 1, Palette.Wood.Light);
        area.Fill(1, 11, 14, 2, Palette.Wood.Base);
        area.Fill(1, 11, 14, 1, Palette.Wood.Light);
        area.Orb(4, 3, 1.7, Palette.Red);
        area.Orb(9, 3, 1.7, Palette.Yellow);
        area.Orb(12, 3, 1.5, Palette.Leaf);
        area.Orb(4, 9, 1.7, Palette.Leaf);
        area.Orb(10, 9, 1.7, Palette.Blossom);
        area.Orb(5, 14, 1.2, Palette.Purple);
        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    private static void Trough(Area area, bool water)
    {
        area.Wood(1, 4, 14, 10, true, 180);
        area.Fill(3, 6, 10, 5, Palette.Wood.Shade);
        area.Frame(3, 6, 10, 5, Palette.Outline);
        if (water)
        {
            area.Fill(4, 7, 8, 3, Palette.Water.Base);
            area.Fill(4, 7, 8, 1, Palette.Water.Light);
            area.Put(6, 8, Palette.Foam);
        }
        else
        {
            area.Fill(4, 7, 8, 3, Palette.Sand.Base);
            area.Speckle(4, 7, 8, 3, Palette.Sand.Light, 181, 3);
            area.Speckle(4, 7, 8, 3, Palette.Sand.Shade, 182, 4);
        }

        area.Fill(1, 14, 14, 1, Palette.Shadow);
    }

    // A hay bale: tightly packed straw with two cords.
    private static void Bale(Area area)
    {
        for (int y = 3; y < 14; y++)
        {
            for (int x = 1; x < 15; x++)
            {
                uint n = Noise.Of(x, y, 183);
                area.Put(x, y, (y % 3 == 0) ? Palette.Sand.Shade : n % 4 == 0 ? Palette.Sand.Light : n % 5 == 0 ? Palette.Sand.Shade : Palette.Sand.Base);
            }
        }

        area.Fill(1, 3, 14, 1, Palette.Sand.Light);
        area.Fill(5, 3, 1, 11, Palette.Wood.Shade);
        area.Fill(10, 3, 1, 11, Palette.Wood.Shade);
        area.Frame(1, 3, 14, 11, Palette.Outline);
        area.Fill(1, 14, 14, 1, Palette.Shadow);
    }

    private static void Bench(Area area)
    {
        area.Wood(0, 5, area.Width, 4, true, 184);
        area.Wood(1, 9, 3, area.Height - 9, false, 185);
        area.Wood(area.Width - 4, 9, 3, area.Height - 9, false, 186);
        area.Fill(1, area.Height - 1, area.Width - 2, 1, Palette.Shadow);
    }

    // A chair seen from the front: a tall back with slats, the seat and four legs.
    private static void Chair(Area area)
    {
        area.Wood(3, 1, 10, 6, true, 187);
        area.Fill(5, 2, 1, 4, Palette.Wood.Shade);
        area.Fill(8, 2, 1, 4, Palette.Wood.Shade);
        area.Fill(11, 2, 1, 4, Palette.Wood.Shade);
        area.Wood(2, 7, 12, 3, true, 188);
        area.Wood(3, 10, 2, 5, false, 189);
        area.Wood(11, 10, 2, 5, false, 190);
        area.Fill(3, 15, 10, 1, Palette.Shadow);
    }

    // A nest box lined with straw; the egg lies in it when the box is full.
    private static void Nest(Area area, bool egg)
    {
        area.Wood(1, 4, 14, 10, true, 191);
        area.Fill(2, 6, 12, 6, Palette.Sand.Shade);
        area.Speckle(2, 6, 12, 6, Palette.Sand.Base, 192, 2);
        area.Speckle(2, 6, 12, 6, Palette.Sand.Light, 193, 5);
        area.Frame(2, 6, 12, 6, Palette.Outline);
        if (egg)
        {
            area.Blob(8, 9, 3, 3, Palette.White);
            area.Put(7, 8, Palette.White.Light);
        }

        area.Fill(1, 14, 14, 1, Palette.Shadow);
    }

    private static void Perch(Area area)
    {
        area.Wood(0, 5, 16, 4, true, 194);
        area.Wood(2, 9, 3, 7, false, 195);
        area.Wood(11, 9, 3, 7, false, 196);
    }

    private static void Gate(Area area)
    {
        area.Wood(0, 0, 16, 16, true, 197);
        area.Fill(1, 4, 14, 1, Palette.Wood.Shade);
        area.Fill(1, 11, 14, 1, Palette.Wood.Shade);
        area.Fill(1, 5, 14, 1, Palette.Wood.Light);
        area.Fill(1, 12, 14, 1, Palette.Wood.Light);
        area.Orb(12.5, 8, 1.3, Palette.Yellow);
    }

    // A stack of hay: a lumpy heap of loose straw.
    private static void Stack(Area area)
    {
        area.Canopy([(8, 6, 4.5), (4.5, 10, 4.5), (11.5, 10, 4.5), (8, 10, 5)], [Palette.Sand.Shade, Palette.Sand.Shade, Palette.Sand.Base, Palette.Sand.Light, Palette.White.Base], 198);
    }
}
