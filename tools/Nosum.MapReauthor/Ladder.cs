// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapReauthor;

/// <summary>The top of the mine ladder (<c>Maps/Mines/mine.png</c>, cell 3, 4): a ladder going down into a dark hole in a stone frame. Drawn in code in the shared palette.</summary>
internal static class Ladder
{
    /// <summary>Draws the ladder into one cell.</summary>
    /// <param name="sheet">The own sheet.</param>
    /// <param name="left">Left of the cell.</param>
    /// <param name="top">Top of the cell.</param>
    public static void Paint(Rgba sheet, int left, int top)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        Rgb frameShade = Materials.Shade(Material.Stone), frame = Materials.Base(Material.Stone), frameLight = Materials.Light(Material.Stone);
        Rgb hole = Materials.Base(Material.Dark), holeDeep = Materials.Shade(Material.Dark);
        Rgb rail = Materials.Base(Material.Wood), rung = Materials.Light(Material.Wood), railShade = Materials.Shade(Material.Wood);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                Rgb color;
                if (x == 0 || y == 0 || x == 15 || y == 15)
                {
                    color = Materials.Outline;
                }
                else if (x < 3 || y < 3 || x > 12 || y > 12)
                {
                    color = x == 1 || y == 1 ? frameLight : x >= 13 || y >= 13 ? frameShade : frame;
                }
                else if (x is 5 or 10)
                {
                    color = x == 5 ? rail : railShade;
                }
                else if (x is > 5 and < 10 && y is 5 or 8 or 11)
                {
                    color = rung;
                }
                else
                {
                    color = y < 6 ? hole : holeDeep;
                }

                int i = (((top + y) * sheet.Width) + left + x) * 4;
                sheet.Pixels[i] = color.R;
                sheet.Pixels[i + 1] = color.G;
                sheet.Pixels[i + 2] = color.B;
                sheet.Pixels[i + 3] = 255;
            }
        }
    }
}
