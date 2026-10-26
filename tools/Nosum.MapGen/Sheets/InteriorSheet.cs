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

    // Floor boards in courses of four rows; each course starts at its own offset, so the seams do not line up.
    private static void Planks(Area area, int variant, bool dark = false)
    {
        Tones t = dark ? new Tones(Palette.Dark.Base, new Rgb(72, 48, 58), new Rgb(96, 66, 74)) : Palette.Wood;
        area.Fill(0, 0, 16, 16, t.Base);
        for (int course = 0; course < 4; course++)
        {
            int y = course * 4;
            area.Fill(0, y, 16, 1, t.Shade);
            int seam = ((course * 7) + (variant * 5)) % 16;
            area.Fill(seam, y + 1, 1, 3, t.Shade);
            area.Fill((seam + 9) % 16, y + 1, 1, 3, t.Shade);
            area.Fill(0, y + 1, 16, 1, t.Light);
        }
    }

    private static void Rug(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Red.Base);
        area.Frame(1, 1, 14, 14, Palette.Yellow.Base);
        area.Frame(3, 3, 10, 10, Palette.Yellow.Base);
        area.Fill(7, 7, 2, 2, Palette.Yellow.Light);
    }

    // Straw on packed earth. Hay makes the straw denser and warmer.
    private static void Straw(Area area, int seed, bool hay = false)
    {
        area.Fill(0, 0, 16, 16, Palette.Dirt.Base);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                uint n = Noise.Of(x, y, 50 + seed);
                int density = hay ? 3 : 7;
                if (((x + (2 * y) + seed) % 9 < 2) && n % density != 0)
                {
                    area.Put(x, y, n % 2 == 0 ? Palette.Sand.Base : Palette.Sand.Light);
                }
                else if (n % 17 == 0)
                {
                    area.Put(x, y, Palette.Dirt.Shade);
                }
            }
        }
    }

    private static void Run(Area area)
    {
        Straw(area, 9);
        area.Blob(4, 11, 2, 1.5, Palette.Stone);
        area.Blob(11, 5, 2, 1.5, Palette.Stone);
    }

    private static void Plaster(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Plaster.Base);
        area.Fill(0, 13, 16, 3, Palette.Plaster.Shade);
        area.Fill(0, 0, 16, 1, Palette.Plaster.Light);
    }

    private static void Wainscot(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        area.Frame(1, 2, 6, 5, Palette.Wood.Shade);
        area.Frame(9, 2, 6, 5, Palette.Wood.Shade);
        area.Fill(0, 0, 16, 1, Palette.Wood.Light);
        area.Fill(0, 15, 16, 1, Palette.Outline);
    }

    private static void Cap(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Stone.Base);
        area.Fill(0, 0, 16, 2, Palette.Stone.Light);
        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    private static void Corner(Area area)
    {
        Plaster(area);
        area.Fill(0, 0, 2, 16, Palette.Plaster.Light);
        area.Fill(14, 0, 2, 16, Palette.Plaster.Shade);
    }

    private static void Window(Area area)
    {
        Plaster(area);
        area.Frame(3, 3, 10, 10, Palette.Wood.Shade);
        area.Fill(4, 4, 8, 8, Palette.Water.Light);
        area.Fill(7, 4, 2, 8, Palette.Wood.Base);
        area.Fill(4, 7, 8, 2, Palette.Wood.Base);
    }

    private static void Door(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        area.Fill(2, 2, 12, 5, Palette.Wood.Shade);
        area.Fill(2, 9, 12, 5, Palette.Wood.Shade);
        area.Frame(0, 0, 16, 16, Palette.Outline);
        area.Fill(11, 8, 2, 2, Palette.Yellow.Light);
    }

    // A bed of 32 × 32 px: the headboard and pillow in the top half, the red blanket with a fold in the bottom half.
    private static void Bed(Area area)
    {
        area.Fill(0, 0, 32, 8, Palette.Wood.Shade);
        area.Fill(3, 2, 12, 5, Palette.White.Base);
        area.Fill(0, 8, 32, 24, Palette.Red.Base);
        area.Fill(0, 8, 32, 2, Palette.White.Base);
        area.Fill(0, 20, 32, 1, Palette.Red.Shade);
        area.Frame(0, 0, 32, 32, Palette.Outline);
    }

    // A table of 32 × 32 px: a top across the top half and four legs in the bottom half.
    private static void Table(Area area)
    {
        area.Fill(0, 2, 32, 10, Palette.Wood.Base);
        area.Fill(0, 2, 32, 2, Palette.Wood.Light);
        area.Frame(0, 2, 32, 10, Palette.Outline);
        area.Fill(2, 12, 3, 19, Palette.Wood.Shade);
        area.Fill(27, 12, 3, 19, Palette.Wood.Shade);
    }

    private static void Stove(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Stone.Base);
        area.Frame(0, 0, 16, 16, Palette.Outline);
        area.Fill(1, 1, 14, 3, Palette.Stone.Shade);
        area.Fill(3, 8, 10, 6, Palette.Dark.Base);
        area.Fill(5, 10, 6, 2, Palette.Yellow.Base);
    }

    private static void Sink(Area area)
    {
        Counter(area);
        area.Blob(8, 9, 4, 2, Palette.Water);
    }

    private static void Counter(Area area)
    {
        area.Fill(0, 0, 16, 6, Palette.Wood.Light);
        area.Fill(0, 6, 16, 10, Palette.Wood.Base);
        area.Frame(0, 0, 16, 16, Palette.Outline);
        area.Fill(7, 8, 1, 6, Palette.Wood.Shade);
    }

    private static void Cupboard(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        area.Frame(1, 1, 14, 14, Palette.Wood.Shade);
        area.Fill(7, 1, 2, 14, Palette.Wood.Shade);
        area.Fill(5, 7, 1, 2, Palette.Yellow.Base);
        area.Fill(10, 7, 1, 2, Palette.Yellow.Base);
        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    private static void Shelf(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        area.Fill(0, 5, 16, 1, Palette.Wood.Light);
        area.Fill(0, 11, 16, 1, Palette.Wood.Light);
        area.Blob(4, 3, 2, 2, Palette.Red);
        area.Blob(9, 3, 2, 2, Palette.Yellow);
        area.Blob(4, 9, 2, 2, Palette.Leaf);
        area.Blob(11, 9, 2, 2, Palette.Blossom);
        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    private static void Trough(Area area, bool water)
    {
        area.Fill(1, 4, 14, 10, Palette.Wood.Base);
        area.Fill(1, 4, 14, 2, Palette.Wood.Light);
        area.Frame(1, 4, 14, 10, Palette.Outline);
        area.Fill(3, 7, 10, 4, water ? Palette.Water.Base : Palette.Sand.Base);
    }

    private static void Bale(Area area)
    {
        area.Fill(1, 3, 14, 11, Palette.Sand.Base);
        area.Fill(1, 3, 14, 2, Palette.Sand.Light);
        area.Fill(1, 11, 14, 2, Palette.Sand.Shade);
        area.Fill(5, 3, 1, 11, Palette.Wood.Base);
        area.Fill(10, 3, 1, 11, Palette.Wood.Base);
        area.Frame(1, 3, 14, 11, Palette.Outline);
    }

    private static void Bench(Area area)
    {
        area.Fill(0, 6, area.Width, 3, Palette.Wood.Light);
        area.Frame(0, 6, area.Width, 3, Palette.Outline);
        area.Fill(1, 9, 2, area.Height - 9, Palette.Wood.Base);
        area.Fill(area.Width - 3, 9, 2, area.Height - 9, Palette.Wood.Base);
    }

    private static void Chair(Area area)
    {
        area.Fill(3, 2, 10, 3, Palette.Wood.Base);
        area.Fill(3, 5, 10, 5, Palette.Wood.Light);
        area.Fill(3, 10, 2, 5, Palette.Wood.Shade);
        area.Fill(11, 10, 2, 5, Palette.Wood.Shade);
        area.Frame(3, 2, 10, 13, Palette.Outline);
    }

    // A nest box; the egg lies in it when the box is full.
    private static void Nest(Area area, bool egg)
    {
        area.Fill(1, 4, 14, 10, Palette.Wood.Base);
        area.Fill(2, 6, 12, 6, Palette.Sand.Base);
        area.Frame(1, 4, 14, 10, Palette.Outline);
        if (egg)
        {
            area.Blob(8, 9, 3, 3, Palette.White);
        }
    }

    private static void Perch(Area area)
    {
        area.Fill(0, 6, 16, 3, Palette.Wood.Light);
        area.Frame(0, 6, 16, 3, Palette.Outline);
        area.Fill(2, 9, 2, 7, Palette.Wood.Base);
        area.Fill(12, 9, 2, 7, Palette.Wood.Base);
    }

    private static void Gate(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        area.Fill(0, 4, 16, 1, Palette.Wood.Shade);
        area.Fill(0, 11, 16, 1, Palette.Wood.Shade);
        area.Frame(0, 0, 16, 16, Palette.Wood.Shade);
        area.Fill(12, 7, 2, 2, Palette.Yellow.Base);
    }

    private static void Stack(Area area)
    {
        area.Blob(8, 8, 7, 6, Palette.Sand);
        area.Fill(3, 9, 10, 1, Palette.Sand.Shade);
    }
}
