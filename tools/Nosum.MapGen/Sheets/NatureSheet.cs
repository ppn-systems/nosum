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

        Tree(Area.Of(canvas, 0, 2, 3), Palette.Leaf, 0);
        Tree(Area.Of(canvas, 2, 2, 3), Palette.Red, 1);
        Tree(Area.Of(canvas, 4, 2, 3), Palette.LeafDark, 2);

        Bush(Area.Of(canvas, 6), 0);
        Bush(Area.Of(canvas, 7), 1);
        Bush(Area.Of(canvas, 8), 2);

        Area.Of(canvas, 9).Blob(8, 10, 5, 4, Palette.Stone);
        Area.Of(canvas, 10, 2).Blob(16, 12, 14, 6, Palette.Stone);
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
        Area.Of(canvas, 29).Blob(8, 9, 6, 7, Palette.Stone);
        Pebbles(Area.Of(canvas, 30));
        Sapling(Area.Of(canvas, 31));
    }

    // A tree over the 2 × 3 block: trunk first, then the canopy over its top.
    private static void Tree(Area area, Tones leaves, int species)
    {
        area.Fill(14, 26, 4, 22, Palette.Wood.Base);
        area.Fill(14, 26, 1, 22, Palette.Wood.Light);
        area.Fill(17, 26, 1, 22, Palette.Wood.Shade);
        if (species == 2)
        {
            for (int y = 1; y <= 29; y++)
            {
                int half = 2 + ((y * 12) / 30);
                area.Fill(16 - half, y, half * 2, 1, leaves.Base);
                area.Put(16 - half, y, leaves.Light);
                area.Put(15 + half, y, leaves.Shade);
            }

            return;
        }

        area.Blob(16, 14, 14, 12, leaves);
    }

    private static void Bush(Area area, int kind)
    {
        area.Blob(8, 9, 6.5, 4.5, Palette.Leaf);
        if (kind == 1)
        {
            area.Put(5, 8, Palette.Red.Base);
            area.Put(10, 10, Palette.Red.Base);
            area.Put(8, 6, Palette.Red.Light);
        }
        else if (kind == 2)
        {
            area.Put(5, 8, Palette.Blossom.Base);
            area.Put(10, 10, Palette.Blossom.Base);
            area.Put(8, 6, Palette.Blossom.Light);
        }
    }

    private static void Stump(Area area)
    {
        area.Blob(8, 11, 6, 4, Palette.Wood);
        area.Blob(8, 9, 5, 2.5, Palette.Wood);
    }

    private static void Log(Area area)
    {
        area.Fill(1, 6, 14, 7, Palette.Wood.Base);
        area.Fill(1, 6, 14, 2, Palette.Wood.Light);
        area.Fill(1, 11, 14, 2, Palette.Wood.Shade);
        area.Frame(1, 6, 14, 7, Palette.Outline);
    }

    // A straight run: two rails across the tile between posts at its ends.
    private static void Straight(Area area, bool horizontal)
    {
        if (horizontal)
        {
            area.Fill(0, 5, 16, 2, Palette.Wood.Base);
            area.Fill(0, 10, 16, 2, Palette.Wood.Base);
            area.Fill(1, 3, 2, 12, Palette.Wood.Light);
            area.Fill(13, 3, 2, 12, Palette.Wood.Light);
            return;
        }

        area.Fill(5, 0, 2, 16, Palette.Wood.Base);
        area.Fill(10, 0, 2, 16, Palette.Wood.Base);
        area.Fill(3, 1, 10, 2, Palette.Wood.Light);
        area.Fill(3, 13, 10, 2, Palette.Wood.Light);
    }

    // A corner post at the middle with rails to the sides named.
    private static void Corner(Area area, bool north, bool east, bool south, bool west)
    {
        if (north)
        {
            area.Fill(5, 0, 2, 4, Palette.Wood.Base);
        }

        if (south)
        {
            area.Fill(5, 8, 2, 8, Palette.Wood.Base);
        }

        if (east)
        {
            area.Fill(8, 5, 8, 2, Palette.Wood.Base);
        }

        if (west)
        {
            area.Fill(0, 5, 4, 2, Palette.Wood.Base);
        }

        area.Fill(4, 4, 4, 4, Palette.Wood.Light);
    }

    private static void Gate(Area area)
    {
        area.Fill(1, 3, 2, 12, Palette.Wood.Light);
        area.Fill(13, 3, 2, 12, Palette.Wood.Light);
        area.Fill(0, 5, 3, 2, Palette.Wood.Base);
        area.Fill(13, 5, 3, 2, Palette.Wood.Base);
        area.Fill(0, 10, 3, 2, Palette.Wood.Base);
        area.Fill(13, 10, 3, 2, Palette.Wood.Base);
    }

    private static void Post(Area area)
    {
        area.Fill(6, 2, 4, 13, Palette.Wood.Base);
        area.Fill(5, 1, 6, 2, Palette.Wood.Light);
    }

    private static void Hedge(Area area)
    {
        area.Fill(0, 2, 16, 13, Palette.Leaf.Base);
        area.Frame(0, 2, 16, 13, Palette.Leaf.Shade);
        for (int y = 3; y < 14; y++)
        {
            for (int x = 1; x < 15; x++)
            {
                if (Noise.Of(x, y, 28) % 7 == 0)
                {
                    area.Put(x, y, Palette.Leaf.Light);
                }
            }
        }
    }

    private static void Pebbles(Area area)
    {
        area.Blob(4, 12, 3, 2, Palette.Stone);
        area.Blob(10, 11, 2.5, 2, Palette.Stone);
        area.Blob(8, 7, 2, 1.5, Palette.Stone);
    }

    private static void Sapling(Area area)
    {
        area.Fill(7, 8, 2, 7, Palette.Wood.Base);
        area.Blob(8, 6, 4, 3, Palette.Leaf);
    }
}
