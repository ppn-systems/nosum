// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapReauthor;

/// <summary>Where a sheet is used.</summary>
internal enum Setting
{
    /// <summary>Outdoor maps.</summary>
    Outdoors,

    /// <summary>House, coop and barn interiors.</summary>
    Indoors,

    /// <summary>The mine's entrance and floors.</summary>
    Mine,
}

/// <summary>
/// Draws one own tile for one placeholder tile of a drawn layer (buildings, trees, cliffs, fences, floors, walls, props).
/// Nothing is copied from the placeholder's pixels. The painter reads three coarse facts from the tile, as a floor plan would give them: which of its
/// 4 × 4 blocks of 4 px hold anything, what <see cref="Material"/> each such block is (from its average colour), and whether the tile is a shadow
/// (dark and translucent). Every pixel of the result is then drawn in code, from the palette in <see cref="Materials"/> (the Kenney palette of the
/// phase 2 pictures, the terrain tones for earth and rock) with its own textures: the silhouette is the blocks smoothed to round shapes, the
/// outline is the shared dark tone, and the light comes from the top left (ADR-040 D1).
/// </summary>
internal static class ObjectPainter
{
    private const int Size = 16;
    private const int Blocks = 4;
    private const int BlockSize = Size / Blocks;

    /// <summary>Paints the own tile that replaces one cell of a placeholder sheet.</summary>
    /// <param name="source">The placeholder sheet.</param>
    /// <param name="sourceX">Left of the cell in the placeholder sheet.</param>
    /// <param name="sourceY">Top of the cell in the placeholder sheet.</param>
    /// <param name="target">The own sheet, with the same layout.</param>
    /// <param name="targetX">Left of the cell in the own sheet.</param>
    /// <param name="targetY">Top of the cell in the own sheet.</param>
    /// <param name="seed">A number that varies the texture noise from tile to tile: the tile's index.</param>
    /// <param name="ground">Draw a floor: a ground tile that is all filled gets a flat texture with no outline and no edge light.</param>
    /// <param name="phase">The animation frame, which shifts the ripples of water; 0 for a still tile.</param>
    /// <param name="setting">Where the sheet is used, which moves some materials (yellow floors indoors are planks, light floors in the mine are earth).</param>
    public static void Paint(Rgba source, int sourceX, int sourceY, Rgba target, int targetX, int targetY, int seed, bool ground, int phase, Setting setting = Setting.Outdoors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        TileFacts facts = TileFacts.Read(source, sourceX, sourceY);
        if (facts.Shadow)
        {
            PaintShadow(facts, target, targetX, targetY);
            return;
        }

        if (facts.OccupiedBlocks == 0)
        {
            return;
        }

        bool full = facts.OccupiedBlocks == Blocks * Blocks;
        bool[] mask = new bool[Size * Size];
        Material[] material = new Material[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                mask[(y * Size) + x] = full || facts.Field(x, y) >= 32;
            }
        }

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (mask[(y * Size) + x])
                {
                    material[(y * Size) + x] = Adjust(facts.NearestMaterial(x + ((int)(Hash(x, y, seed) % 3) - 1), y + ((int)(Hash(y, x, seed + 7) % 3) - 1)), setting, ground && full);
                }
            }
        }

        bool flat = ground && full;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = (y * Size) + x;
                if (!mask[i])
                {
                    continue;
                }

                Material m = material[i];
                Rgb color = Texture(m, x, y, seed, flat, phase);
                if (!flat)
                {
                    bool up = Filled(mask, x, y - 1), down = Filled(mask, x, y + 1), left = Filled(mask, x - 1, y), right = Filled(mask, x + 1, y);
                    if (!(up && down && left && right))
                    {
                        color = Materials.OutlineOf(m);
                    }
                    else if (!Filled(mask, x, y - 2) || !Filled(mask, x - 2, y))
                    {
                        color = Materials.Light(m);
                    }
                    else if (!Filled(mask, x, y + 2) || !Filled(mask, x + 2, y))
                    {
                        color = Materials.Shade(m);
                    }
                }

                Put(target, targetX + x, targetY + y, color, 255);
            }
        }
    }

    private static Material Adjust(Material m, Setting setting, bool floor)
        => setting switch
        {
            Setting.Indoors when m is Material.Sand or Material.Dirt => Material.Wall,
            Setting.Mine when floor && m is Material.Rock => Material.Dirt,
            Setting.Mine when m is Material.Wall or Material.Sand or Material.Brick or Material.Red or Material.RoofRed => Material.Dirt,
            _ => m,
        };

    // Outside the tile the outline continues what the edge pixel is, so a tile that fills its border has no outline there.
    private static bool Filled(bool[] mask, int x, int y) => mask[(Math.Clamp(y, 0, Size - 1) * Size) + Math.Clamp(x, 0, Size - 1)];

    private static void PaintShadow(TileFacts facts, Rgba target, int targetX, int targetY)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (facts.AlphaField(x, y) >= 32)
                {
                    Put(target, targetX + x, targetY + y, new Rgb(24, 18, 32), facts.ShadowAlpha);
                }
            }
        }
    }

    private static void Put(Rgba target, int x, int y, Rgb color, int alpha)
    {
        int i = ((y * target.Width) + x) * 4;
        target.Pixels[i] = color.R;
        target.Pixels[i + 1] = color.G;
        target.Pixels[i + 2] = color.B;
        target.Pixels[i + 3] = (byte)alpha;
    }

    private static uint Hash(int a, int b, int c)
    {
        uint x = unchecked(((uint)a * 374761393u) + ((uint)b * 668265263u) + ((uint)c * 2147483647u));
        x = unchecked((x ^ (x >> 13)) * 1274126177u);
        return x ^ (x >> 16);
    }

    // The texture of a material at a pixel. Patterns that must continue across tiles (courses, planks, shingles) depend on the position in the tile only.
    private static Rgb Texture(Material m, int x, int y, int seed, bool floor, int phase)
    {
        Rgb shade = Materials.Shade(m), baseTone = Materials.Base(m), light = Materials.Light(m);
        uint n = Hash(x, y, seed);
        switch (m)
        {
            case Material.Leaf:
            case Material.LeafDark:
            case Material.Blossom:
                uint c = Hash((x / 2) + (((y / 2) & 1) * 3), y / 2, seed) % 7;
                return c == 0 ? light : c == 1 ? shade : baseTone;
            case Material.Wood:
                if (floor)
                {
                    return y % 4 == 3 || (x + ((y / 4) * 5)) % 16 == 0 ? shade : n % 11 == 0 ? light : baseTone;
                }

                return (x + (y / 5)) % 4 == 0 ? shade : (n % 17 == 0 ? light : baseTone);
            case Material.Wall:
                if (floor)
                {
                    return y % 4 == 3 || (x + ((y / 4) * 7)) % 16 == 0 ? shade : n % 13 == 0 ? light : baseTone;
                }

                return x % 5 == 4 ? shade : (n % 13 == 0 ? light : baseTone);
            case Material.Rock:
                if (floor)
                {
                    return n % 9 == 0 ? shade : n % 7 == 0 ? light : baseTone;
                }

                int row = (y + (seed % 3)) % 5;
                return row == 4 ? shade : (row == 0 && n % 3 == 0 ? light : (n % 11 == 0 ? shade : baseTone));
            case Material.Stone:
                return y % 6 == 5 || (x + ((y / 6) * 5)) % 8 == 7 ? shade : (n % 9 == 0 ? light : baseTone);
            case Material.RoofRed:
            case Material.RoofBlue:
                int course = y % 4;
                return course == 3 ? shade : (course == 0 && (x + ((y / 4) * 2)) % 4 < 2 ? light : baseTone);
            case Material.Brick:
                return y % 4 == 3 || (x + ((y / 4) * 3)) % 6 == 5 ? shade : baseTone;
            case Material.Water:
                return ((x + (phase * 2) + (y / 2)) % 8 < 1 && y % 3 == 0) ? light : baseTone;
            case Material.Sand:
            case Material.Dirt:
                return n % 9 == 0 ? shade : (n % 7 == 0 ? light : baseTone);
            case Material.White:
                return n % 6 == 0 ? shade : (n % 3 == 0 ? light : baseTone);
            default:
                return n % 5 == 0 ? shade : baseTone;
        }
    }

    /// <summary>The coarse facts the painter reads from one placeholder tile.</summary>
    private sealed class TileFacts
    {
        private readonly bool[] _occupied = new bool[Blocks * Blocks];
        private readonly Material[] _material = new Material[Blocks * Blocks];
        private readonly int[] _alpha = new int[Blocks * Blocks];

        public int OccupiedBlocks { get; private set; }

        public bool Shadow { get; private set; }

        public int ShadowAlpha { get; private set; }

        public static TileFacts Read(Rgba sheet, int left, int top)
        {
            TileFacts facts = new();
            int[] count = new int[Blocks * Blocks];
            double[] red = new double[Blocks * Blocks], green = new double[Blocks * Blocks], blue = new double[Blocks * Blocks];
            int[] seen = new int[Blocks * Blocks];
            double any = 0, anyAlpha = 0, anyDark = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = ((((top + y) * sheet.Width) + left + x)) * 4;
                    int alpha = sheet.Pixels[i + 3];
                    int block = (y / BlockSize * Blocks) + (x / BlockSize);
                    if (alpha > 0)
                    {
                        seen[block]++;
                        any++;
                        anyAlpha += alpha;
                        if (Math.Max(sheet.Pixels[i], Math.Max(sheet.Pixels[i + 1], sheet.Pixels[i + 2])) < 64)
                        {
                            anyDark++;
                        }
                    }

                    if (alpha >= 128)
                    {
                        count[block]++;
                        red[block] += sheet.Pixels[i];
                        green[block] += sheet.Pixels[i + 1];
                        blue[block] += sheet.Pixels[i + 2];
                    }
                }
            }

            // A shadow: nearly everything it draws is dark and translucent.
            if (any > 0 && anyDark / any > 0.9 && anyAlpha / any < 200)
            {
                facts.Shadow = true;
                facts.ShadowAlpha = (int)Math.Round(anyAlpha / any);
                for (int b = 0; b < facts._alpha.Length; b++)
                {
                    facts._alpha[b] = seen[b] >= 2 ? 1 : 0;
                }

                return facts;
            }

            int best = 0;
            for (int b = 0; b < count.Length; b++)
            {
                best = count[b] > count[best] ? b : best;
            }

            for (int b = 0; b < count.Length; b++)
            {
                if (count[b] >= 2 || (b == best && count[b] > 0))
                {
                    facts._occupied[b] = true;
                    facts.OccupiedBlocks++;
                    facts._material[b] = Materials.Classify(red[b] / count[b], green[b] / count[b], blue[b] / count[b]);
                }
            }

            return facts;
        }

        /// <summary>The smoothed occupancy of a pixel, 0 to 64: the blocks' centres interpolated, the edge blocks extended past the tile.</summary>
        public int Field(int x, int y) => Interpolate(_occupied, x, y);

        public int AlphaField(int x, int y)
        {
            bool[] set = new bool[_alpha.Length];
            for (int i = 0; i < set.Length; i++)
            {
                set[i] = _alpha[i] != 0;
            }

            return Interpolate(set, x, y);
        }

        public Material NearestMaterial(int x, int y)
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int b = 0; b < _occupied.Length; b++)
            {
                if (!_occupied[b])
                {
                    continue;
                }

                // The block centres are at 1.5 + 4k; work in twice the pixels so the centre is an integer.
                int dx = (x * 2) - (((b % Blocks) * BlockSize * 2) + 3), dy = (y * 2) - (((b / Blocks) * BlockSize * 2) + 3);
                int d = (dx * dx) + (dy * dy);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = b;
                }
            }

            return _material[best];
        }

        private static int Interpolate(bool[] set, int x, int y)
        {
            // The pixel's centre in eighths of a block, measured from the first block's centre, kept inside the grid of centres.
            int u = Math.Clamp((2 * x) - 3, 0, 24), v = Math.Clamp((2 * y) - 3, 0, 24);
            int x0 = u / 8, y0 = v / 8, fx = u % 8, fy = v % 8;
            int x1 = Math.Min(Blocks - 1, x0 + 1), y1 = Math.Min(Blocks - 1, y0 + 1);
            int top = (Get(set, x0, y0) * (8 - fx)) + (Get(set, x1, y0) * fx);
            int bottom = (Get(set, x0, y1) * (8 - fx)) + (Get(set, x1, y1) * fx);
            return ((top * (8 - fy)) + (bottom * fy));
        }

        private static int Get(bool[] set, int x, int y) => set[(y * Blocks) + x] ? 1 : 0;
    }
}
