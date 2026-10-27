// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// The mine's cave sheet (docs/adr/ADR-042 P2). Tile 0 is the floor and tile 1 the wall, the two tiles <see cref="MineGenerator"/> reads.
/// The rest: floor variants (2 to 4), the wall's edges (16 to 31, one per set of open sides, north 1, east 2, south 4, west 8), the ladder-up
/// and elevator markers (32 to 34) and decor (35 to 47). The walls sit on the Buildings layer and are the only solid tiles; see <c>cave.tileset</c>.
/// </summary>
internal static class CaveSheet
{
    /// <summary>Tiles the sheet defines.</summary>
    public const int TileCount = 48;

    /// <summary>Draws the sheet.</summary>
    /// <param name="canvas">The sheet's picture.</param>
    public static void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        Floor(Area.Of(canvas, 0), 0);
        Wall(Area.Of(canvas, 1), 0);
        Floor(Area.Of(canvas, 2), 1);
        Floor(Area.Of(canvas, 3), 2);
        Floor(Area.Of(canvas, 4), 3);
        for (int open = 0; open < 16; open++)
        {
            Wall(Area.Of(canvas, 16 + open), open);
        }

        Ladder(Area.Of(canvas, 32));
        Elevator(Area.Of(canvas, 33), false);
        Elevator(Area.Of(canvas, 34), true);
        for (int i = 35; i < TileCount; i++)
        {
            Floor(Area.Of(canvas, i), 0);
            Decor(Area.Of(canvas, i), i);
        }
    }

    // Flagstone: smooth patches of two tones, short lit and shaded flecks, and a variant 1 crack, 2 pebbles or 3 moss.
    private static void Floor(Area area, int variant)
    {
        Tones t = Palette.CaveFloor;
        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                uint n = Noise.Of(x, y, 10 + variant);
                int patch = Shading.Smooth(x, y, 61, 8) + (int)(n % 36) - 18;
                Rgb c = patch < 100 ? Shading.Mix(t.Base, t.Shade, 140) : patch > 175 ? Shading.Mix(t.Base, t.Light, 80) : t.Base;
                area.Put(x, y, n % 19 == 0 ? t.Light : n % 13 == 0 ? t.Shade : c);
            }
        }

        switch (variant)
        {
            case 1:
                int[] cx = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
                int[] cy = [3, 4, 5, 5, 6, 7, 8, 8, 9, 10, 11];
                for (int i = 0; i < cx.Length; i++)
                {
                    area.Put(cx[i], cy[i], Shading.Mix(t.Base, Palette.Dark.Base, 140));
                    area.Put(cx[i] + 1, cy[i], Shading.Mix(t.Base, t.Light, 90));
                }

                break;
            case 2:
                Pebble(area, 4, 5);
                Pebble(area, 11, 10);
                Pebble(area, 13, 12);
                break;
            case 3:
                Moss(area, 3, 11);
                Moss(area, 12, 4);
                break;
        }
    }

    // A wall: rough dark rock in blocks with lit edges and cracks; an open side gets a lit rim (north and west, toward the light) or a
    // dark one with a face (east and south).
    private static void Wall(Area area, int open)
    {
        Tones t = Palette.CaveWall;
        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                uint n = Noise.Of(x, y, 40);
                int block = Shading.Smooth(x, y, 62, 4) + (int)(n % 40) - 20;
                Rgb c = block < 90 ? Shading.Mix(t.Base, t.Shade, 160) : block > 160 ? Shading.Mix(t.Base, t.Light, 90) : t.Base;
                area.Put(x, y, n % 31 == 0 ? t.Light : c);
            }
        }

        // Cracks: a jagged dark line with a lit pixel beside it.
        for (int i = 0; i < 5; i++)
        {
            area.Put(3 + i, 4 + (i / 2), t.Shade);
            area.Put(10 + (i / 2), 9 + i, t.Shade);
            area.Put(4 + i, 5 + (i / 2), Shading.Mix(t.Base, t.Light, 80));
        }

        if ((open & 1) != 0)
        {
            area.Fill(0, 0, SheetInfo.TileSize, 1, Shading.Mix(t.Light, Palette.White.Base, 70));
            area.Fill(0, 1, SheetInfo.TileSize, 1, t.Light);
            area.Fill(0, 2, SheetInfo.TileSize, 1, Shading.Mix(t.Base, t.Light, 120));
        }

        if ((open & 8) != 0)
        {
            area.Fill(0, 0, 1, SheetInfo.TileSize, Shading.Mix(t.Light, Palette.White.Base, 70));
            area.Fill(1, 0, 1, SheetInfo.TileSize, t.Light);
        }

        if ((open & 2) != 0)
        {
            area.Fill(SheetInfo.TileSize - 3, 0, 3, SheetInfo.TileSize, t.Shade);
            area.Fill(SheetInfo.TileSize - 1, 0, 1, SheetInfo.TileSize, Palette.Dark.Shade);
        }

        if ((open & 4) != 0)
        {
            area.Fill(0, SheetInfo.TileSize - 4, SheetInfo.TileSize, 4, t.Shade);
            area.Fill(0, SheetInfo.TileSize - 4, SheetInfo.TileSize, 1, Shading.Mix(t.Shade, t.Light, 90));
            area.Fill(0, SheetInfo.TileSize - 1, SheetInfo.TileSize, 1, Palette.Dark.Shade);
        }
    }

    // A ladder going up: two rails and four rungs on the floor.
    private static void Ladder(Area area)
    {
        Floor(area, 0);
        area.Fill(3, 1, 2, 14, Palette.Wood.Base);
        area.Fill(11, 1, 2, 14, Palette.Wood.Base);
        area.Fill(3, 1, 1, 14, Palette.Wood.Light);
        area.Fill(11, 1, 1, 14, Palette.Wood.Light);
        foreach (int y in new[] { 3, 7, 11 })
        {
            area.Fill(5, y, 6, 2, Palette.Wood.Light);
        }
    }

    // The elevator pad: a stone frame, an arrow pointing up and the pad lit when the lift is at this floor.
    private static void Elevator(Area area, bool lit)
    {
        Floor(area, 0);
        area.Frame(1, 1, 14, 14, Palette.Stone.Shade);
        area.Fill(2, 2, 12, 12, Palette.Stone.Base);
        area.Fill(4, 4, 8, 8, lit ? Palette.Yellow.Light : Palette.Stone.Light);
        Rgb arrow = lit ? Palette.Yellow.Base : Palette.Stone.Shade;
        area.Fill(7, 5, 2, 1, arrow);
        area.Fill(6, 6, 4, 1, arrow);
        area.Fill(5, 7, 6, 1, arrow);
        area.Fill(7, 8, 2, 4, arrow);
    }

    // Decor on the floor: 35 pebbles, 36 crystal, 37 mushroom, 38 bones, 39 rock pile, 40 stalagmite, 41 puddle, 42 torch, 43 web, 44 moss,
    // 45 crack, 46 dust, 47 stalactite.
    private static void Decor(Area area, int index)
    {
        switch (index)
        {
            case 35:
                area.Canopy([(4, 11, 2.6), (10, 6, 2.8), (12, 12, 2.2)], Palette.StoneRamp, 35);
                break;
            case 36:
                // A cluster of crystals: tall faceted spikes, lit on the left face.
                Crystal(area, 8, 14, 3, 11);
                Crystal(area, 4, 14, 2, 6);
                Crystal(area, 12, 14, 2, 7);
                break;
            case 37:
                area.Fill(7, 9, 2, 5, Palette.White.Base);
                area.Put(7, 9, Palette.White.Light);
                area.Blob(8, 7, 4.5, 3.2, Palette.Red);
                area.Put(6, 6, Palette.White.Light);
                area.Put(9, 7, Palette.White.Light);
                area.Put(10, 5, Palette.White.Base);
                break;
            case 38:
                area.Fill(4, 8, 8, 1, Palette.White.Base);
                area.Fill(4, 10, 6, 1, Palette.White.Base);
                area.Blob(3, 8, 1.5, 1.5, Palette.White);
                area.Blob(11, 10, 1.5, 1.5, Palette.White);
                break;
            case 39:
                area.Canopy([(5, 11, 4.2), (11, 10.5, 3.8), (8, 7.5, 3.6)], Palette.StoneRamp, 39);
                break;
            case 40:
                for (int y = 3; y < 15; y++)
                {
                    int half = 1 + ((y - 3) / 2);
                    for (int x = 8 - half; x < 8 + half; x++)
                    {
                        Rgb c = x == 8 - half ? Palette.CaveFloor.Light : x >= 8 + half - 1 ? Palette.Dark.Base : Palette.CaveWall.Light;
                        area.Put(x, y, y > 12 ? Palette.Dark.Base : c);
                    }
                }

                area.Fill(4, 14, 8, 1, Palette.Dark.Shade);
                break;
            case 41:
                area.Blob(8, 11, 6.5, 3.2, Palette.Water);
                area.Put(6, 10, Palette.Foam);
                area.Put(7, 10, Palette.Foam);
                area.Put(10, 12, Palette.Water.Light);
                break;
            case 42:
                for (int y = 0; y < SheetInfo.TileSize; y++)
                {
                    for (int x = 0; x < SheetInfo.TileSize; x++)
                    {
                        double d = Math.Sqrt(((x - 8) * (x - 8)) + ((y - 6) * (y - 6)));
                        if (d < 7.5 && ((x + y) & 1) == 0)
                        {
                            area.Put(x, y, Shading.Mix(Palette.CaveFloor.Base, Palette.Yellow.Base, d < 4 ? 130 : 70));
                        }
                    }
                }

                area.Fill(7, 8, 2, 6, Palette.Wood.Base);
                area.Put(7, 8, Palette.Wood.Light);
                area.Fill(6, 13, 4, 1, Palette.Outline);
                area.Fill(6, 7, 4, 1, Palette.Stone.Shade);
                area.Blob(8, 4.5, 2.4, 3, Palette.Yellow);
                area.Fill(8, 3, 1, 3, Palette.White.Light);
                break;
            case 43:
                for (int i = 0; i < SheetInfo.TileSize; i++)
                {
                    area.Put(i, i, Palette.Stone.Light);
                    area.Put(SheetInfo.TileSize - 1 - i, i, Palette.Stone.Light);
                }

                break;
            case 44:
                area.Canopy([(5, 9, 3.5), (10, 8, 4), (8, 11, 3.5)], Palette.OakRamp, 44);
                break;
            case 45:
                for (int i = 0; i < 6; i++)
                {
                    area.Put(10 + i, 4 + i, Palette.CaveWall.Shade);
                }

                break;
            case 46:
                for (int y = 0; y < SheetInfo.TileSize; y++)
                {
                    for (int x = 0; x < SheetInfo.TileSize; x++)
                    {
                        if (Noise.Of(x, y, 46) % 23 == 0)
                        {
                            area.Put(x, y, Palette.CaveFloor.Light);
                        }
                    }
                }

                break;
            case 47:
                for (int y = 0; y < 12; y++)
                {
                    int half = 4 - (y / 3);
                    area.Fill(8 - half, y + 1, half * 2, 1, Palette.CaveWall.Base);
                }

                break;
        }
    }

    // A crystal spike: a lit left face, a dark right face and a bright tip, standing on the floor at (x, base).
    private static void Crystal(Area area, int x, int bottom, int half, int height)
    {
        for (int i = 0; i < height; i++)
        {
            int y = bottom - i;
            int w = i > height - 3 ? Math.Max(1, half - (i - (height - 3))) : half;
            for (int dx = -w; dx < w; dx++)
            {
                Rgb c = dx == -w || dx == w - 1 ? Palette.Outline : dx < 0 ? Palette.Purple.Light : Palette.Purple.Shade;
                area.Put(x + dx, y, i == height - 1 ? Palette.Outline : c);
            }
        }

        area.Put(x - 1, bottom - height + 2, Palette.White.Light);
        area.Put(x - 1, bottom - (height / 2), Palette.White.Base);
    }

    // A pebble: two grey pixels with a shadow under them, small enough not to read as an object.
    private static void Pebble(Area area, int x, int y)
    {
        Tones t = Palette.CaveFloor;
        area.Put(x, y, Shading.Mix(t.Base, t.Light, 190));
        area.Put(x + 1, y, Shading.Mix(t.Base, t.Light, 110));
        area.Put(x, y + 1, Shading.Mix(t.Base, t.Shade, 190));
        area.Put(x + 1, y + 1, Shading.Mix(t.Base, t.Shade, 120));
    }

    // A patch of moss: a few muted green pixels.
    private static void Moss(Area area, int x, int y)
    {
        Rgb green = Shading.Mix(Palette.CaveFloor.Base, Palette.OakRamp[2], 120);
        Rgb dark = Shading.Mix(Palette.CaveFloor.Shade, Palette.OakRamp[1], 100);
        area.Put(x, y, green);
        area.Put(x + 1, y, green);
        area.Put(x - 1, y + 1, dark);
        area.Put(x, y + 1, green);
        area.Put(x + 1, y + 1, dark);
        area.Put(x, y - 1, dark);
    }
}
