// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapReauthor;

/// <summary>
/// The own cobble path, drawn in code: there is no generated set for it (ADR-040, phase 3). The fill is flat-laid cobbles in courses; the corner tiles
/// join it to grass with a rounded, mortar-lined edge, in the same 16 corner combinations as a <see cref="WangSet"/>.
/// </summary>
internal static class PathArt
{
    private const int Size = OwnTileset.TileSize;

    private static readonly Rgb s_mortar = new(88, 78, 80);
    private static readonly Rgb s_shade = new(132, 120, 116);
    private static readonly Rgb s_stone = new(168, 156, 144);
    private static readonly Rgb s_light = new(198, 186, 170);

    // Courses of the cobbles: where each starts, and how far its joints are shifted so they do not line up.
    private static readonly int[] s_courseTop = [0, 5, 11];
    private static readonly int[] s_courseShift = [0, 4, 2];

    /// <summary>Draws one texture variant of the cobble fill.</summary>
    /// <param name="target">The tileset image.</param>
    /// <param name="left">Left of the tile.</param>
    /// <param name="top">Top of the tile.</param>
    /// <param name="variant">The variant, 0 to <see cref="OwnTileset.Variants"/> - 1; they differ in which cobbles are light and dark.</param>
    public static void Fill(Rgba target, int left, int top, int variant)
    {
        ArgumentNullException.ThrowIfNull(target);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Put(target, left + x, top + y, Cobble(x, y, variant));
            }
        }
    }

    /// <summary>Draws one corner tile: the cobble where the corners are lower, the grass tile where they are upper, a rounded edge between.</summary>
    /// <param name="target">The tileset image.</param>
    /// <param name="left">Left of the tile.</param>
    /// <param name="top">Top of the tile.</param>
    /// <param name="code">The corner code (<see cref="WangSet.Code"/>): 1 north-west, 2 north-east, 4 south-west, 8 south-east are grass.</param>
    /// <param name="grass">The grass fill, 16 × 16 at (<paramref name="grassX"/>, <paramref name="grassY"/>).</param>
    /// <param name="grassX">Left of the grass fill in <paramref name="grass"/>.</param>
    /// <param name="grassY">Top of the grass fill in <paramref name="grass"/>.</param>
    public static void Corner(Rgba target, int left, int top, int code, Rgba grass, int grassX, int grassY)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(grass);

        bool[] isGrass = new bool[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // The weight of the grass corners at the pixel's centre, in 1/1024, roughened by a little noise so the edge is not a smooth curve.
                int fx = ((2 * x) + 1) * 32, fy = ((2 * y) + 1) * 32;
                int nw = (code & 1) != 0 ? 1 : 0, ne = (code & 2) != 0 ? 1 : 0, sw = (code & 4) != 0 ? 1 : 0, se = (code & 8) != 0 ? 1 : 0;
                int w = (((nw * (1024 - fx)) + (ne * fx)) * (1024 - fy) / 1024) + (((sw * (1024 - fx)) + (se * fx)) * fy / 1024);
                int noise = (int)(Hash(x, y, code) % 129) - 64;
                isGrass[(y * Size) + x] = w + noise > 512;
            }
        }

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size) + x;
                if (isGrass[i])
                {
                    Array.Copy(grass.Pixels, (((grassY + y) * grass.Width) + grassX + x) * 4, target.Pixels, (((top + y) * target.Width) + left + x) * 4, 4);
                    continue;
                }

                bool edge = (x > 0 && isGrass[i - 1]) || (x < Size - 1 && isGrass[i + 1]) || (y > 0 && isGrass[i - Size]) || (y < Size - 1 && isGrass[i + Size]);
                Put(target, left + x, top + y, edge ? s_mortar : Cobble(x, y, 0));
            }
        }
    }

    private static Rgb Cobble(int x, int y, int variant)
    {
        int course = y < s_courseTop[1] ? 0 : y < s_courseTop[2] ? 1 : 2;
        int row = y - s_courseTop[course];
        int along = (x + s_courseShift[course]) % Size;
        bool jointX = along % 8 == 0;
        if (row == 0 || jointX)
        {
            return s_mortar;
        }

        // Each cobble has its own tone; the first pixel row and column inside it catch the light, the last ones are in shade.
        int stone = (along / 8) + (course * 2) + (variant * 3);
        uint tone = Hash(stone, course, variant) % 5;
        Rgb body = tone == 0 ? s_light : tone == 1 ? s_shade : s_stone;
        int height = course == 1 ? 6 : 5;
        if (row == 1 || along % 8 == 1)
        {
            return s_light;
        }

        if (row == height - 1 || along % 8 == 7)
        {
            return s_shade;
        }

        return Hash(x, y, variant) % 13 == 0 ? s_shade : body;
    }

    private static void Put(Rgba target, int x, int y, Rgb color)
    {
        int i = ((y * target.Width) + x) * 4;
        target.Pixels[i] = color.R;
        target.Pixels[i + 1] = color.G;
        target.Pixels[i + 2] = color.B;
        target.Pixels[i + 3] = 255;
    }

    private static uint Hash(int a, int b, int c)
    {
        uint x = unchecked(((uint)a * 374761393u) + ((uint)b * 668265263u) + ((uint)c * 2147483647u));
        x = unchecked((x ^ (x >> 13)) * 1274126177u);
        return x ^ (x >> 16);
    }
}
