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

    // Stone with speckles. Variant 1 has a crack, 2 pebbles and 3 moss.
    private static void Floor(Area area, int variant)
    {
        Tones t = Palette.CaveFloor;
        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                uint n = Noise.Of(x, y, 10 + variant);
                area.Put(x, y, n % 11 == 0 ? t.Shade : n % 17 == 0 ? t.Light : t.Base);
            }
        }

        switch (variant)
        {
            case 1:
                int[] cx = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
                int[] cy = [3, 4, 5, 5, 6, 7, 8, 8, 9, 10, 11];
                for (int i = 0; i < cx.Length; i++)
                {
                    area.Put(cx[i], cy[i], t.Shade);
                }

                break;
            case 2:
                area.Blob(4, 5, 2, 1.5, t);
                area.Blob(11, 10, 2, 2, t);
                break;
            case 3:
                area.Blob(3, 11, 2, 2, Palette.Leaf);
                area.Blob(12, 4, 2, 2, Palette.Leaf);
                break;
        }
    }

    // A wall: solid rock; an open side gets a lit rim (north and west, toward the light) or a shaded one (east and south).
    private static void Wall(Area area, int open)
    {
        Tones t = Palette.CaveWall;
        for (int y = 0; y < SheetInfo.TileSize; y++)
        {
            for (int x = 0; x < SheetInfo.TileSize; x++)
            {
                uint n = Noise.Of(x, y, 40);
                area.Put(x, y, n % 9 == 0 ? t.Shade : n % 19 == 0 ? t.Light : t.Base);
            }
        }

        if ((open & 1) != 0)
        {
            area.Fill(0, 0, SheetInfo.TileSize, 2, t.Light);
        }

        if ((open & 8) != 0)
        {
            area.Fill(0, 0, 2, SheetInfo.TileSize, t.Light);
        }

        if ((open & 2) != 0)
        {
            area.Fill(SheetInfo.TileSize - 2, 0, 2, SheetInfo.TileSize, t.Shade);
        }

        if ((open & 4) != 0)
        {
            area.Fill(0, SheetInfo.TileSize - 2, SheetInfo.TileSize, 2, t.Shade);
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
                area.Blob(4, 11, 2, 1.5, Palette.Stone);
                area.Blob(10, 6, 2, 2, Palette.Stone);
                area.Blob(12, 12, 1.5, 1.5, Palette.Stone);
                break;
            case 36:
                area.Blob(8, 8, 3, 6, Palette.Purple);
                break;
            case 37:
                area.Fill(7, 9, 2, 5, Palette.White.Base);
                area.Blob(8, 7, 4, 3, Palette.Red);
                break;
            case 38:
                area.Fill(4, 8, 8, 1, Palette.White.Base);
                area.Fill(4, 10, 6, 1, Palette.White.Base);
                area.Blob(3, 8, 1.5, 1.5, Palette.White);
                area.Blob(11, 10, 1.5, 1.5, Palette.White);
                break;
            case 39:
                area.Blob(5, 11, 4, 3, Palette.Stone);
                area.Blob(11, 10, 3, 3, Palette.Stone);
                break;
            case 40:
                for (int y = 3; y < 15; y++)
                {
                    int half = 1 + ((y - 3) / 2);
                    area.Fill(8 - half, y, half * 2, 1, Palette.CaveWall.Base);
                }

                break;
            case 41:
                area.Blob(8, 11, 6, 3, Palette.Water);
                break;
            case 42:
                area.Fill(7, 8, 2, 6, Palette.Wood.Base);
                area.Blob(8, 6, 2, 2, Palette.Yellow);
                break;
            case 43:
                for (int i = 0; i < SheetInfo.TileSize; i++)
                {
                    area.Put(i, i, Palette.Stone.Light);
                    area.Put(SheetInfo.TileSize - 1 - i, i, Palette.Stone.Light);
                }

                break;
            case 44:
                area.Blob(8, 8, 6, 5, Palette.Leaf);
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
}
