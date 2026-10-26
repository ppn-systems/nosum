// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// The town sheet (docs/adr/ADR-042 P2): house walls, roofs, the door, windows, shop counters, signs, a bridge with planks, crates, barrels,
/// lamp posts and awnings, drawn in code. Walls and roofs are 16 × 16 tiles; the roof tiles and the awnings are the outlined edges the house
/// block needs. Indices are listed in <c>town.tileset</c>, which holds the traits.
/// </summary>
internal static class TownSheet
{
    /// <summary>Tiles the sheet defines.</summary>
    public const int TileCount = 38;

    /// <summary>Draws the sheet.</summary>
    /// <param name="canvas">The sheet's picture.</param>
    public static void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        Plaster(Area.Of(canvas, 0));
        Timber(Area.Of(canvas, 1));
        Window(Area.Of(canvas, 2), false);
        Door(Area.Of(canvas, 3));

        Roof(Area.Of(canvas, 4), true, false, true, false);
        Roof(Area.Of(canvas, 5), true, false, false, false);
        Roof(Area.Of(canvas, 6), true, true, false, false);
        Roof(Area.Of(canvas, 7), false, false, true, true);
        Roof(Area.Of(canvas, 8), false, false, false, true);
        Roof(Area.Of(canvas, 9), false, true, false, true);

        Chimney(Area.Of(canvas, 10));
        Sign(Area.Of(canvas, 11));
        Area.Of(canvas, 12).Fill(7, 0, 2, 16, Palette.Wood.Base);
        Counter(Area.Of(canvas, 13), 0);
        Counter(Area.Of(canvas, 14), 1);
        Counter(Area.Of(canvas, 15), 2);

        Planks(Area.Of(canvas, 16), false);
        Planks(Area.Of(canvas, 17), true);
        Rail(Area.Of(canvas, 18));
        Planks(Area.Of(canvas, 19), false);

        Crate(Area.Of(canvas, 20), false);
        Crate(Area.Of(canvas, 21), true);
        Lamp(Area.Of(canvas, 22), false);
        Lamp(Area.Of(canvas, 23), false, post: true);
        Lamp(Area.Of(canvas, 24), true);
        FlowerBox(Area.Of(canvas, 25));
        Awning(Area.Of(canvas, 26), 0);
        Awning(Area.Of(canvas, 27), 1);
        Awning(Area.Of(canvas, 28), 2);
        Barrel(Area.Of(canvas, 29));
        Bench(Area.Of(canvas, 30));
        Window(Area.Of(canvas, 31), true);

        Wares(Area.Of(canvas, 32));
        Ridge(Area.Of(canvas, 33));
        Base(Area.Of(canvas, 34));
        Step(Area.Of(canvas, 35));
        Pillar(Area.Of(canvas, 36));
        Bracket(Area.Of(canvas, 37));
    }

    private static void Plaster(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Plaster.Base);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                if (Noise.Of(x, y, 3) % 13 == 0)
                {
                    area.Put(x, y, Palette.Plaster.Shade);
                }
            }
        }

        area.Fill(0, 14, 16, 2, Palette.Plaster.Shade);
    }

    private static void Timber(Area area)
    {
        Plaster(area);
        area.Fill(0, 0, 2, 16, Palette.Wood.Shade);
        area.Fill(14, 0, 2, 16, Palette.Wood.Shade);
        area.Fill(0, 7, 16, 2, Palette.Wood.Shade);
    }

    private static void Window(Area area, bool shuttered)
    {
        Plaster(area);
        if (shuttered)
        {
            area.Fill(0, 4, 3, 8, Palette.Wood.Base);
            area.Fill(13, 4, 3, 8, Palette.Wood.Base);
        }

        area.Frame(4, 4, 8, 8, Palette.Wood.Shade);
        area.Fill(5, 5, 6, 6, Palette.Water.Light);
        area.Fill(7, 5, 2, 6, Palette.Wood.Base);
        area.Fill(5, 7, 6, 2, Palette.Wood.Base);
    }

    private static void Door(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        for (int x = 3; x < 16; x += 4)
        {
            area.Fill(x, 0, 1, 16, Palette.Wood.Shade);
        }

        area.Frame(0, 0, 16, 16, Palette.Wood.Shade);
        area.Fill(11, 8, 2, 2, Palette.Sand.Base);
    }

    // Shingles in courses, offset every other course; the sides named are outlined.
    private static void Roof(Area area, bool left, bool right, bool top, bool bottom)
    {
        area.Fill(0, 0, 16, 16, Palette.RoofRed.Base);
        for (int y = 0; y < 16; y++)
        {
            int offset = (y / 4) % 2 == 0 ? 0 : 4;
            for (int x = 0; x < 16; x++)
            {
                if (((x + offset) % 8) == 0)
                {
                    area.Put(x, y, Palette.RoofRed.Shade);
                }
                else if (y % 4 == 0)
                {
                    area.Put(x, y, Palette.RoofRed.Light);
                }
            }
        }

        if (top)
        {
            area.Fill(0, 0, 16, 1, Palette.Outline);
        }

        if (bottom)
        {
            area.Fill(0, 15, 16, 1, Palette.Outline);
            area.Fill(0, 13, 16, 2, Palette.RoofRed.Shade);
        }

        if (left)
        {
            area.Fill(0, 0, 1, 16, Palette.Outline);
        }

        if (right)
        {
            area.Fill(15, 0, 1, 16, Palette.Outline);
        }
    }

    private static void Ridge(Area area)
    {
        Roof(area, false, false, false, false);
        area.Fill(0, 0, 16, 4, Palette.RoofRed.Light);
        area.Fill(0, 4, 16, 1, Palette.Outline);
    }

    private static void Chimney(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Brick.Base);
        for (int y = 0; y < 16; y += 4)
        {
            area.Fill(0, y, 16, 1, Palette.Brick.Shade);
            area.Fill((y / 4) % 2 == 0 ? 4 : 10, y + 1, 1, 3, Palette.Brick.Shade);
        }

        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    private static void Sign(Area area)
    {
        area.Fill(1, 4, 14, 8, Palette.Wood.Base);
        area.Frame(1, 4, 14, 8, Palette.Outline);
        area.Fill(3, 6, 10, 1, Palette.Dark.Base);
        area.Fill(3, 9, 7, 1, Palette.Dark.Base);
    }

    private static void Counter(Area area, int end)
    {
        area.Fill(0, 6, 16, 3, Palette.Wood.Light);
        area.Fill(0, 9, 16, 7, Palette.Wood.Base);
        area.Fill(0, 15, 16, 1, Palette.Outline);
        for (int x = 3; x < 16; x += 5)
        {
            area.Fill(x, 10, 1, 5, Palette.Wood.Shade);
        }

        if (end == 0)
        {
            area.Fill(0, 6, 2, 10, Palette.Wood.Shade);
        }
        else if (end == 2)
        {
            area.Fill(14, 6, 2, 10, Palette.Wood.Shade);
        }
    }

    private static void Wares(Area area)
    {
        Counter(area, 1);
        area.Blob(4, 4, 2, 2, Palette.Red);
        area.Blob(9, 4, 2, 2, Palette.Yellow);
        area.Blob(13, 4, 2, 2, Palette.Leaf);
    }

    // A horizontal plank deck; the planks run across the tile and a seam falls every fourth row.
    private static void Planks(Area area, bool knots)
    {
        area.Fill(0, 0, 16, 16, Palette.Wood.Base);
        for (int y = 3; y < 16; y += 4)
        {
            area.Fill(0, y, 16, 1, Palette.Wood.Shade);
        }

        if (knots)
        {
            area.Put(5, 6, Palette.Wood.Shade);
            area.Put(6, 6, Palette.Wood.Shade);
            area.Put(11, 13, Palette.Wood.Shade);
            area.Put(12, 13, Palette.Wood.Shade);
        }
    }

    private static void Rail(Area area)
    {
        area.Fill(0, 6, 16, 2, Palette.Wood.Light);
        area.Fill(0, 10, 16, 2, Palette.Wood.Light);
        area.Fill(1, 4, 2, 12, Palette.Wood.Base);
        area.Fill(13, 4, 2, 12, Palette.Wood.Base);
    }

    private static void Crate(Area area, bool open)
    {
        if (open)
        {
            area.Fill(1, 8, 14, 7, Palette.Wood.Base);
            area.Blob(5, 6, 2, 2, Palette.Red);
            area.Blob(10, 6, 2, 2, Palette.Yellow);
            area.Blob(8, 4, 2, 2, Palette.Leaf);
            area.Frame(1, 8, 14, 7, Palette.Outline);
            return;
        }

        area.Fill(1, 2, 14, 13, Palette.Wood.Base);
        area.Frame(1, 2, 14, 13, Palette.Outline);
        area.Fill(1, 7, 14, 1, Palette.Wood.Shade);
        area.Fill(1, 11, 14, 1, Palette.Wood.Shade);
        area.Fill(7, 2, 2, 13, Palette.Wood.Shade);
    }

    private static void Lamp(Area area, bool lit, bool post = false)
    {
        if (post)
        {
            area.Fill(6, 4, 4, 12, Palette.Stone.Base);
            area.Fill(4, 12, 8, 4, Palette.Stone.Shade);
            return;
        }

        area.Fill(7, 6, 2, 10, Palette.Stone.Shade);
        area.Fill(5, 1, 6, 5, Palette.Stone.Light);
        area.Frame(5, 1, 6, 5, Palette.Outline);
        area.Fill(6, 2, 4, 3, lit ? Palette.Yellow.Light : Palette.Stone.Base);
        if (lit)
        {
            area.Blob(8, 4, 7, 6, Palette.Yellow);
            area.Fill(6, 2, 4, 3, Palette.Yellow.Light);
        }
    }

    private static void FlowerBox(Area area)
    {
        area.Fill(0, 8, 16, 6, Palette.Wood.Base);
        area.Frame(0, 8, 16, 6, Palette.Outline);
        area.Fill(1, 6, 14, 2, Palette.Leaf.Base);
        area.Put(3, 5, Palette.Red.Base);
        area.Put(7, 4, Palette.Purple.Base);
        area.Put(11, 5, Palette.Red.Base);
        area.Put(14, 4, Palette.Yellow.Base);
    }

    // Striped cloth; the three parts join with the stripe phase shifted, and the outer one is shaded.
    private static void Awning(Area area, int part)
    {
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int phase = (x + (part * 2)) / 4;
                Rgb c = phase % 2 == 0 ? Palette.RoofRed.Base : Palette.White.Base;
                area.Put(x, y, y == 11 && (x % 4) == 1 ? Palette.Outline : c);
            }
        }

        area.Fill(0, 0, 16, 1, Palette.Outline);
    }

    private static void Barrel(Area area)
    {
        area.Fill(3, 1, 10, 14, Palette.Wood.Base);
        area.Fill(3, 4, 10, 1, Palette.Wood.Shade);
        area.Fill(3, 11, 10, 1, Palette.Wood.Shade);
        area.Fill(4, 1, 2, 14, Palette.Wood.Light);
        area.Frame(3, 1, 10, 14, Palette.Outline);
    }

    private static void Bench(Area area)
    {
        area.Fill(0, 6, 16, 3, Palette.Wood.Light);
        area.Frame(0, 6, 16, 3, Palette.Outline);
        area.Fill(1, 9, 2, 7, Palette.Wood.Base);
        area.Fill(13, 9, 2, 7, Palette.Wood.Base);
    }

    private static void Base(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Stone.Base);
        for (int y = 0; y < 16; y += 5)
        {
            area.Fill(0, y, 16, 1, Palette.Stone.Shade);
            area.Fill((y * 3) % 16, y, 1, 5, Palette.Stone.Shade);
        }
    }

    private static void Step(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Stone.Light);
        area.Fill(0, 6, 16, 1, Palette.Stone.Shade);
        area.Fill(0, 12, 16, 1, Palette.Stone.Shade);
        area.Fill(6, 0, 1, 6, Palette.Stone.Shade);
        area.Fill(11, 6, 1, 6, Palette.Stone.Shade);
    }

    private static void Pillar(Area area)
    {
        area.Fill(5, 0, 6, 16, Palette.Stone.Base);
        area.Fill(5, 0, 1, 16, Palette.Stone.Light);
        area.Frame(5, 0, 6, 16, Palette.Outline);
    }

    private static void Bracket(Area area)
    {
        area.Fill(7, 0, 2, 4, Palette.Stone.Shade);
        area.Fill(2, 3, 12, 1, Palette.Stone.Shade);
        area.Fill(4, 5, 8, 6, Palette.Wood.Base);
        area.Frame(4, 5, 8, 6, Palette.Outline);
        area.Fill(6, 8, 4, 1, Palette.Dark.Base);
    }
}
