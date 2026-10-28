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
        Roof(Area.Of(canvas, 5), false, false, true, false);
        Roof(Area.Of(canvas, 6), true, false, false, true);
        Roof(Area.Of(canvas, 7), false, false, false, true);
        Roof(Area.Of(canvas, 8), false, true, false, true);
        Roof(Area.Of(canvas, 9), false, true, true, false);

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

    // Plaster: warm mottled render, a lit top edge and a shaded foot.
    private static void Plaster(Area area)
    {
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int mottle = Shading.Smooth(x, y, 3, 8) + (int)(Noise.Of(x, y, 4) % 50) - 25;
                area.Put(x, y, mottle < 100 ? Shading.Mix(Palette.Plaster.Base, Palette.Plaster.Shade, 90) : mottle > 190 ? Shading.Mix(Palette.Plaster.Base, Palette.Plaster.Light, 80) : Palette.Plaster.Base);
            }
        }

        area.Speckle(0, 0, 16, 14, Palette.Plaster.Shade, 5, 19);
        area.Fill(0, 0, 16, 1, Palette.Plaster.Light);
        area.Fill(0, 14, 16, 1, Palette.Plaster.Shade);
        area.Fill(0, 15, 16, 1, Shading.Mix(Palette.Plaster.Shade, Palette.Outline, 110));
    }

    // Half-timbered: wooden posts and a rail over the plaster, each bevelled.
    private static void Timber(Area area)
    {
        Plaster(area);
        area.Wood(0, 0, 3, 16, false, 20, false);
        area.Wood(13, 0, 3, 16, false, 21, false);
        area.Wood(0, 6, 16, 3, true, 22, false);
        area.Fill(3, 9, 10, 1, Shading.Mix(Palette.Plaster.Base, Palette.Outline, 90));
    }

    // A window: a frame with a sill, glass with a sky gradient and a glint, a cross of bars; shutters when asked.
    private static void Window(Area area, bool shuttered)
    {
        Plaster(area);
        if (shuttered)
        {
            area.Wood(0, 3, 3, 10, false, 30);
            area.Wood(13, 3, 3, 10, false, 31);
            area.Fill(1, 5, 1, 1, Palette.Wood.Shade);
            area.Fill(14, 5, 1, 1, Palette.Wood.Shade);
        }

        area.Frame(3, 3, 10, 10, Palette.Outline);
        for (int y = 4; y < 12; y++)
        {
            for (int x = 4; x < 12; x++)
            {
                area.Put(x, y, Shading.Mix(Palette.Water.Light, Palette.Water.Base, (y - 4) * 24));
            }
        }

        area.Fill(4, 4, 8, 1, Palette.Wood.Light);
        area.Fill(7, 4, 2, 8, Palette.Wood.Base);
        area.Fill(4, 7, 8, 2, Palette.Wood.Base);
        area.Fill(7, 4, 1, 8, Palette.Wood.Light);
        area.Fill(4, 7, 8, 1, Palette.Wood.Light);
        area.Put(5, 5, Palette.White.Light);
        area.Put(5, 6, Palette.White.Light);
        area.Put(9, 9, Palette.White.Light);
        area.Fill(2, 13, 12, 2, Palette.Stone.Base);
        area.Fill(2, 13, 12, 1, Palette.Stone.Light);
        area.Fill(2, 15, 12, 1, Palette.Outline);
    }

    // A door of planks in a frame, with iron straps, a handle and a dark lintel.
    private static void Door(Area area)
    {
        area.Wood(0, 0, 16, 16, false, 40);
        for (int x = 4; x < 15; x += 4)
        {
            area.Fill(x, 1, 1, 14, Palette.Wood.Shade);
        }

        area.Fill(1, 1, 14, 2, Shading.Mix(Palette.Wood.Shade, Palette.Outline, 120));
        area.Fill(1, 5, 14, 1, Palette.Dark.Light);
        area.Fill(1, 11, 14, 1, Palette.Dark.Light);
        area.Orb(12, 8.5, 1.3, Palette.Yellow);
    }

    // Shingles in courses of four rows, offset every other course: each shingle has a lit top, a dark joint and its own tone.
    private static void Roof(Area area, bool left, bool right, bool top, bool bottom)
    {
        for (int y = 0; y < 16; y++)
        {
            int course = y / 4;
            int offset = course % 2 == 0 ? 0 : 4;
            int row = y % 4;
            for (int x = 0; x < 16; x++)
            {
                int along = (x + offset) % 8;
                uint tone = Noise.Of(((x + offset) / 8) + 5, course, 31) % 4;
                Rgb baseColor = tone == 0 ? Palette.RoofRed.Light : tone == 1 ? Palette.RoofRed.Shade : Palette.RoofRed.Base;
                Rgb c = row == 0 ? Shading.Mix(baseColor, Palette.RoofRed.Light, 120)
                    : row == 3 ? Shading.Mix(baseColor, Palette.RoofRed.Shade, 150) : baseColor;
                if (along == 0)
                {
                    c = Shading.Mix(Palette.RoofRed.Shade, Palette.Outline, 70);
                }

                area.Put(x, y, c);
            }
        }

        if (top)
        {
            area.Fill(0, 0, 16, 1, Palette.Outline);
        }

        if (bottom)
        {
            area.Fill(0, 12, 16, 3, Palette.RoofRed.Shade);
            area.Fill(0, 15, 16, 1, Palette.Outline);
            area.Speckle(0, 12, 16, 3, Palette.RoofRed.Base, 32, 4);
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
        area.Fill(0, 0, 16, 1, Palette.Yellow.Light);
        area.Fill(0, 3, 16, 1, Palette.RoofRed.Base);
        area.Fill(0, 4, 16, 1, Palette.Outline);
    }

    // A brick chimney: courses of bricks with their own tones, a stone cap on top.
    private static void Chimney(Area area)
    {
        for (int y = 0; y < 16; y++)
        {
            int course = y / 4;
            int offset = course % 2 == 0 ? 0 : 3;
            for (int x = 0; x < 16; x++)
            {
                int along = (x + offset) % 6;
                uint tone = Noise.Of(((x + offset) / 6) + 5, course, 33) % 3;
                Rgb c = tone == 0 ? Palette.Brick.Light : tone == 1 ? Palette.Brick.Shade : Palette.Brick.Base;
                if (y % 4 == 3 || along == 5)
                {
                    c = Palette.Mortar;
                }

                area.Put(x, y, c);
            }
        }

        area.Fill(0, 0, 16, 3, Palette.Stone.Base);
        area.Fill(0, 0, 16, 1, Palette.Stone.Light);
        area.Fill(0, 3, 16, 1, Palette.Outline);
        area.Fill(13, 4, 3, 12, Palette.Brick.Shade);
        area.Frame(0, 0, 16, 16, Palette.Outline);
    }

    // A signboard on two hooks, with painted lines.
    private static void Sign(Area area)
    {
        Plaster(area);
        area.Wood(1, 4, 14, 9, true, 50);
        area.Fill(4, 6, 8, 1, Palette.Yellow.Base);
        area.Fill(3, 9, 6, 1, Palette.Yellow.Base);
        area.Put(10, 9, Palette.Yellow.Light);
        area.Put(12, 9, Palette.Yellow.Light);
        area.Put(3, 2, Palette.Stone.Shade);
        area.Put(12, 2, Palette.Stone.Shade);
        area.Fill(3, 3, 1, 1, Palette.Outline);
        area.Fill(12, 3, 1, 1, Palette.Outline);
    }

    private static void Counter(Area area, int end)
    {
        Plaster(area);
        area.Wood(0, 9, 16, 7, true, 60);
        for (int x = 3; x < 16; x += 5)
        {
            area.Fill(x, 10, 1, 5, Palette.Wood.Shade);
            area.Fill(x + 1, 10, 1, 5, Palette.Wood.Light);
        }

        area.Wood(0, 5, 16, 4, true, 61);
        area.Fill(0, 5, 16, 1, Palette.Wood.Light);
        area.Fill(0, 6, 16, 1, Palette.Wood.Light);
        if (end == 0)
        {
            area.Wood(0, 5, 2, 11, false, 62);
        }
        else if (end == 2)
        {
            area.Wood(14, 5, 2, 11, false, 63);
        }
    }

    private static void Wares(Area area)
    {
        Counter(area, 1);
        area.Orb(4, 3.5, 2, Palette.Red);
        area.Orb(9, 3.5, 2, Palette.Yellow);
        area.Orb(13, 3.5, 2, Palette.Leaf);
    }

    // A plank deck: boards across the tile, each with its own tone, joints between them and nails at the ends.
    private static void Planks(Area area, bool knots)
    {
        for (int board = 0; board < 4; board++)
        {
            int y = board * 4;
            uint tone = Noise.Of(board, 0, 70) % 3;
            Tones t = Palette.Wood;
            Rgb c = tone == 0 ? t.Light : tone == 1 ? t.Base : Shading.Mix(t.Base, t.Shade, 70);
            area.Fill(0, y, 16, 4, c);
            area.Speckle(0, y, 16, 3, t.Shade, 71 + board, 9);
            area.Fill(0, y, 16, 1, Shading.Mix(c, t.Light, 120));
            area.Fill(0, y + 3, 16, 1, t.Shade);
            area.Put(1, y + 1, Palette.Outline);
            area.Put(14, y + 1, Palette.Outline);
        }

        if (knots)
        {
            area.Fill(5, 6, 2, 1, Palette.Wood.Shade);
            area.Fill(11, 13, 2, 1, Palette.Wood.Shade);
            area.Put(5, 7, Palette.Wood.Light);
        }
    }

    private static void Rail(Area area)
    {
        area.Wood(0, 5, 16, 3, true, 80);
        area.Wood(0, 10, 16, 3, true, 81);
        area.Wood(1, 3, 3, 12, false, 82);
        area.Wood(12, 3, 3, 12, false, 83);
        area.Fill(1, 15, 14, 1, Palette.Shadow);
    }

    private static void Crate(Area area, bool open)
    {
        if (open)
        {
            area.Wood(1, 8, 14, 7, true, 90);
            area.Orb(5, 6.5, 2.2, Palette.Red);
            area.Orb(10, 6.5, 2.2, Palette.Yellow);
            area.Orb(7.5, 4.5, 2.2, Palette.Leaf);
            area.Wood(1, 10, 14, 5, true, 91);
            area.Fill(1, 15, 14, 1, Palette.Shadow);
            return;
        }

        area.Wood(1, 2, 14, 13, true, 92);
        area.Fill(2, 6, 12, 1, Palette.Wood.Shade);
        area.Fill(2, 10, 12, 1, Palette.Wood.Shade);
        area.Wood(1, 2, 3, 13, false, 93);
        area.Wood(12, 2, 3, 13, false, 94);
        area.Fill(1, 15, 14, 1, Palette.Shadow);
    }

    private static void Lamp(Area area, bool lit, bool post = false)
    {
        if (post)
        {
            area.Bevel(6, 3, 4, 12, Palette.Stone);
            area.Bevel(4, 12, 8, 3, Palette.Stone);
            area.Frame(4, 12, 8, 3, Palette.Outline);
            area.Put(8, 15, Palette.Outline);
            return;
        }

        if (lit)
        {
            area.Blob(8, 4, 7.5, 6.5, Palette.Yellow);
            area.Fill(5, 2, 6, 4, Palette.Yellow.Light);
        }

        area.Fill(7, 6, 2, 10, Palette.Stone.Shade);
        area.Fill(7, 6, 1, 10, Palette.Stone.Base);
        area.Bevel(5, 13, 6, 3, Palette.Stone);
        area.Frame(5, 13, 6, 3, Palette.Outline);
        area.Bevel(5, 1, 6, 5, Palette.Stone);
        area.Frame(5, 1, 6, 5, Palette.Outline);
        area.Fill(6, 2, 4, 3, lit ? Palette.Yellow.Light : Palette.Yellow.Shade);
        area.Put(6, 2, lit ? Palette.White.Light : Palette.Yellow.Base);
    }

    private static void FlowerBox(Area area)
    {
        area.Wood(0, 8, 16, 6, true, 100);
        area.Fill(0, 14, 16, 1, Palette.Shadow);
        area.Canopy([(3, 6.5, 3), (8, 6, 3.5), (13, 6.5, 3)], Palette.OakRamp, 101, true);
        foreach ((int x, int y, int k) in new[] { (3, 4, 0), (7, 3, 1), (11, 4, 0), (14, 3, 2), (5, 6, 2), (9, 6, 0) })
        {
            Tones f = k == 0 ? Palette.Red : k == 1 ? Palette.Purple : Palette.Yellow;
            area.Put(x, y, f.Base);
            area.Put(x, y - 1, f.Light);
        }
    }

    // Striped cloth with a scalloped hem; the three parts join with the stripe phase shifted.
    private static void Awning(Area area, int part)
    {
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int phase = (x + (part * 2)) / 4;
                bool red = phase % 2 == 0;
                Rgb c = red ? Palette.RoofRed.Base : Palette.White.Base;
                if (y < 2)
                {
                    c = red ? Palette.RoofRed.Light : Palette.White.Light;
                }
                else if (y > 8)
                {
                    c = red ? Palette.RoofRed.Shade : Palette.White.Shade;
                }

                // The scallop: each stripe's hem hangs a pixel lower in its middle.
                int hem = ((x + (part * 2)) % 4 == 1 || (x + (part * 2)) % 4 == 2) ? 11 : 10;
                if (y > hem)
                {
                    continue;
                }

                area.Put(x, y, y == hem ? Palette.Outline : c);
            }
        }

        area.Fill(0, 0, 16, 1, Palette.Outline);
        area.Speckle(0, 1, 16, 8, Palette.White.Shade, 110, 17);
    }

    // A barrel: staves bulging in the middle, two iron hoops, a lit left side and a dark top ring.
    private static void Barrel(Area area)
    {
        for (int y = 1; y < 15; y++)
        {
            int half = 5 - (y < 4 || y > 11 ? 1 : 0);
            for (int x = 8 - half; x < 8 + half; x++)
            {
                Rgb c = x == 8 - half || x == 7 + half ? Palette.Outline : x < 6 ? Palette.Wood.Light : x > 9 ? Palette.Wood.Shade : Palette.Wood.Base;
                if ((x & 3) == 1 && c.Equals(Palette.Wood.Base))
                {
                    c = Shading.Mix(Palette.Wood.Base, Palette.Wood.Shade, 90);
                }

                area.Put(x, y, y == 1 || y == 14 ? Palette.Outline : c);
            }
        }

        area.Fill(3, 4, 10, 1, Palette.Stone.Shade);
        area.Fill(3, 11, 10, 1, Palette.Stone.Shade);
        area.Fill(3, 5, 10, 1, Palette.Stone.Light);
        area.Fill(4, 2, 8, 1, Palette.Wood.Shade);
    }

    private static void Bench(Area area)
    {
        area.Wood(0, 5, 16, 4, true, 120);
        area.Wood(1, 9, 3, 6, false, 121);
        area.Wood(12, 9, 3, 6, false, 122);
        area.Fill(1, 15, 14, 1, Palette.Shadow);
    }

    // The stone base of a wall: ashlar blocks, lit on their tops.
    private static void Base(Area area)
    {
        for (int y = 0; y < 16; y++)
        {
            int course = y / 5;
            for (int x = 0; x < 16; x++)
            {
                int along = (x + (course * 6)) % 12;
                uint tone = Noise.Of(((x + (course * 6)) / 12) + 3, course, 40) % 3;
                Rgb c = tone == 0 ? Palette.Stone.Light : tone == 1 ? Palette.Stone.Base : Shading.Mix(Palette.Stone.Base, Palette.Stone.Shade, 70);
                if (y % 5 == 0 || along == 0)
                {
                    c = Palette.Stone.Shade;
                }
                else if (y % 5 == 1)
                {
                    c = Shading.Mix(c, Palette.White.Light, 70);
                }

                area.Put(x, y, c);
            }
        }
    }

    private static void Step(Area area)
    {
        area.Fill(0, 0, 16, 16, Palette.Stone.Light);
        area.Speckle(0, 0, 16, 16, Palette.Stone.Base, 130, 6);
        area.Fill(0, 6, 16, 1, Palette.Stone.Shade);
        area.Fill(0, 12, 16, 1, Palette.Stone.Shade);
        area.Fill(0, 7, 16, 1, Palette.Stone.Base);
        area.Fill(6, 0, 1, 6, Palette.Stone.Shade);
        area.Fill(11, 6, 1, 6, Palette.Stone.Shade);
        area.Fill(0, 13, 16, 3, Palette.Stone.Base);
        area.Fill(0, 15, 16, 1, Palette.Stone.Shade);
    }

    private static void Pillar(Area area)
    {
        Plaster(area);
        Door(area);
        area.Bevel(0, 0, 2, 16, Palette.Stone);
        area.Bevel(14, 0, 2, 16, Palette.Stone);
        area.Frame(0, 0, 2, 16, Palette.Outline);
        area.Frame(14, 0, 2, 16, Palette.Outline);
    }

    // A hanging shop sign on its bracket.
    private static void Bracket(Area area)
    {
        area.Fill(7, 0, 2, 4, Palette.Stone.Shade);
        area.Fill(2, 3, 12, 1, Palette.Stone.Shade);
        area.Fill(2, 2, 12, 1, Palette.Stone.Base);
        area.Wood(4, 5, 8, 6, true, 140);
        area.Fill(6, 7, 4, 1, Palette.Yellow.Base);
        area.Fill(6, 9, 3, 1, Palette.Yellow.Base);
        area.Put(4, 4, Palette.Outline);
        area.Put(11, 4, Palette.Outline);
    }
}
