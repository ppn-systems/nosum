// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// The <c>Paths</c> marker sheet (docs/adr/ADR-042 P2): the logic layer's marks for trees, weeds, stones, twigs, grass and forage spots, one picture
/// each, at the indices <c>NodeSites</c> reads (9 to 11, 13 to 18, 22, 24 to 26). The layer is never drawn, so the pictures only let an
/// editor see the mark; the other indices stay empty.
/// </summary>
internal static class MarkerSheet
{
    /// <summary>Tiles the sheet defines: indices 0 to 26.</summary>
    public const int TileCount = 27;

    /// <summary>Draws the sheet.</summary>
    /// <param name="canvas">The sheet's picture.</param>
    public static void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        Tree(Area.Of(canvas, 9), Palette.Leaf);
        Tree(Area.Of(canvas, 10), Palette.Red);
        Tree(Area.Of(canvas, 11), Palette.LeafDark);

        Weed(Area.Of(canvas, 13), 0);
        Weed(Area.Of(canvas, 14), 1);
        Weed(Area.Of(canvas, 15), 2);

        Area.Of(canvas, 16).Blob(8, 10, 4, 3, Palette.Stone);
        Area.Of(canvas, 17).Blob(8, 9, 6, 5, Palette.Stone);

        Twig(Area.Of(canvas, 18));

        Strokes(Area.Of(canvas, 22));

        Flower(Area.Of(canvas, 24));
        Berry(Area.Of(canvas, 25));
        Mushroom(Area.Of(canvas, 26));
    }

    private static void Tree(Area area, Tones leaves)
    {
        area.Fill(7, 10, 2, 5, Palette.Wood.Base);
        area.Blob(8, 6, 5, 5, leaves);
    }

    private static void Weed(Area area, int bend)
    {
        area.Fill(7, 6, 1, 9, Palette.Leaf.Base);
        area.Put(5 + bend, 6, Palette.Leaf.Light);
        area.Put(9 - bend, 9, Palette.Leaf.Light);
        area.Put(8 + bend, 4, Palette.Leaf.Light);
    }

    private static void Twig(Area area)
    {
        for (int i = 0; i < 10; i++)
        {
            area.Put(3 + i, 12 - i, Palette.Wood.Base);
            area.Put(3 + i, 13 - i, Palette.Wood.Shade);
        }
    }

    private static void Strokes(Area area)
    {
        area.Fill(4, 8, 1, 6, Palette.Leaf.Base);
        area.Fill(7, 6, 1, 8, Palette.Leaf.Light);
        area.Fill(10, 8, 1, 6, Palette.Leaf.Base);
    }

    private static void Flower(Area area)
    {
        area.Fill(7, 9, 1, 6, Palette.Leaf.Base);
        area.Blob(8, 7, 2, 2, Palette.Yellow);
    }

    private static void Berry(Area area)
    {
        area.Blob(6, 8, 2, 2, Palette.Red);
        area.Blob(10, 8, 2, 2, Palette.Red);
        area.Blob(8, 11, 2, 2, Palette.Red);
    }

    private static void Mushroom(Area area)
    {
        area.Fill(7, 9, 2, 5, Palette.White.Base);
        area.Blob(8, 7, 4, 3, Palette.Purple);
    }
}
