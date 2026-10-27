// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// Shared painting helpers of the drawn sheets: smooth tileable noise, colour mixing and the shaded shapes (bevels, grain, speckles,
/// lobed canopies) that give the flat tones depth. Everything is deterministic: the same inputs always give the same pixels.
/// </summary>
internal static class Shading
{
    /// <summary>Returns the colour <paramref name="t"/>/256 of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    /// <param name="a">The colour at 0.</param>
    /// <param name="b">The colour at 256.</param>
    /// <param name="t">The weight of <paramref name="b"/>, 0 to 256.</param>
    /// <returns>The mix.</returns>
    public static Rgb Mix(Rgb a, Rgb b, int t)
    {
        t = Math.Clamp(t, 0, 256);
        return new Rgb(
            (byte)(((a.R * (256 - t)) + (b.R * t)) >> 8),
            (byte)(((a.G * (256 - t)) + (b.G * t)) >> 8),
            (byte)(((a.B * (256 - t)) + (b.B * t)) >> 8));
    }

    /// <summary>
    /// Smooth value noise, 0 to 255, that repeats every 16 pixels in both directions, so a 16 × 16 tile made from it joins its neighbours
    /// without a seam. The lattice has a point every <paramref name="cellX"/> by <paramref name="cellY"/> pixels (divisors of 16).
    /// </summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="seed">Salt of the texture.</param>
    /// <param name="cellX">Lattice spacing across.</param>
    /// <param name="cellY">Lattice spacing down.</param>
    /// <returns>The noise, 0 to 255.</returns>
    public static int Smooth(int x, int y, int seed, int cellX, int cellY)
    {
        x = ((x % 16) + 16) % 16;
        y = ((y % 16) + 16) % 16;
        int nx = 16 / cellX, ny = 16 / cellY;
        int gx = x / cellX, gy = y / cellY;
        int fx = (x % cellX) * 256 / cellX, fy = (y % cellY) * 256 / cellY;
        fx = fx * fx * (768 - (2 * fx)) >> 16;
        fy = fy * fy * (768 - (2 * fy)) >> 16;
        int a = Lattice(gx % nx, gy % ny, seed), b = Lattice((gx + 1) % nx, gy % ny, seed);
        int c = Lattice(gx % nx, (gy + 1) % ny, seed), d = Lattice((gx + 1) % nx, (gy + 1) % ny, seed);
        int top = ((a * (256 - fx)) + (b * fx)) >> 8;
        int bottom = ((c * (256 - fx)) + (d * fx)) >> 8;
        return ((top * (256 - fy)) + (bottom * fy)) >> 8;
    }

    /// <summary>Smooth tileable noise with a square lattice.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="seed">Salt.</param>
    /// <param name="cell">Lattice spacing.</param>
    /// <returns>The noise, 0 to 255.</returns>
    public static int Smooth(int x, int y, int seed, int cell) => Smooth(x, y, seed, cell, cell);

    /// <summary>Paints a filled rectangle bevelled: light on the top and left, shade on the bottom and right.</summary>
    /// <param name="area">The area.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="w">Width.</param>
    /// <param name="h">Height.</param>
    /// <param name="t">The tones.</param>
    public static void Bevel(this Area area, int x, int y, int w, int h, Tones t)
    {
        area.Fill(x, y, w, h, t.Base);
        area.Fill(x, y, w, 1, t.Light);
        area.Fill(x, y, 1, h, t.Light);
        area.Fill(x, y + h - 1, w, 1, t.Shade);
        area.Fill(x + w - 1, y + 1, 1, h - 1, t.Shade);
    }

    /// <summary>Scatters single pixels of a colour over a rectangle, about one in <paramref name="modulo"/>.</summary>
    /// <param name="area">The area.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="w">Width.</param>
    /// <param name="h">Height.</param>
    /// <param name="color">The colour.</param>
    /// <param name="seed">Salt.</param>
    /// <param name="modulo">One pixel in this many.</param>
    public static void Speckle(this Area area, int x, int y, int w, int h, Rgb color, int seed, uint modulo)
    {
        for (int yy = y; yy < y + h; yy++)
        {
            for (int xx = x; xx < x + w; xx++)
            {
                if (Noise.Of(xx, yy, seed) % modulo == 0)
                {
                    area.Put(xx, yy, color);
                }
            }
        }
    }

    /// <summary>
    /// Paints wood: a base with grain lines running along the length, a lit first edge and a shaded last edge, then the outline when asked.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="w">Width.</param>
    /// <param name="h">Height.</param>
    /// <param name="horizontal">Whether the grain runs across.</param>
    /// <param name="seed">Salt of the grain.</param>
    /// <param name="outline">Whether to draw the outline.</param>
    public static void Wood(this Area area, int x, int y, int w, int h, bool horizontal, int seed, bool outline = true)
    {
        Tones t = Palette.Wood;
        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                int across = horizontal ? yy : xx;
                int along = horizontal ? xx : yy;
                uint n = Noise.Of(across, along / 3, seed);
                Rgb c = n % 5 == 0 ? t.Shade : n % 7 == 0 ? t.Light : t.Base;
                if (Noise.Of(across, along, seed + 1) % 23 == 0)
                {
                    c = t.Shade;
                }

                area.Put(x + xx, y + yy, c);
            }
        }

        if (horizontal)
        {
            area.Fill(x, y, w, 1, t.Light);
            area.Fill(x, y + h - 1, w, 1, t.Shade);
        }
        else
        {
            area.Fill(x, y, 1, h, t.Light);
            area.Fill(x + w - 1, y, 1, h, t.Shade);
        }

        if (outline)
        {
            area.Frame(x, y, w, h, Palette.Outline);
        }
    }

    /// <summary>A lit orb: an outlined ellipse with a soft gradient and a specular dot, the shape of fruit and gems.</summary>
    /// <param name="area">The area.</param>
    /// <param name="cx">Centre column.</param>
    /// <param name="cy">Centre row.</param>
    /// <param name="r">Radius.</param>
    /// <param name="t">The tones.</param>
    public static void Orb(this Area area, double cx, double cy, double r, Tones t)
    {
        area.Blob(cx, cy, r, r, t);
        area.Put((int)Math.Round(cx - (r * 0.4)), (int)Math.Round(cy - (r * 0.4)), Palette.White.Light);
    }

    /// <summary>
    /// A canopy of overlapping lobes, drawn back to front: each lobe is lit on its upper left and dark on its lower right with a dark rim
    /// where it overlaps the lobe behind, leaf specks over the lit parts, and one outline around the whole silhouette.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <param name="lobes">The lobes as centre column, centre row and radius, back to front.</param>
    /// <param name="ramp">Five tones from the deepest shade to the brightest highlight.</param>
    /// <param name="seed">Salt of the leaf texture.</param>
    /// <param name="bleed">Whether the shape runs on past the area's edge, so that edge gets no outline (a hedge that meets its neighbours).</param>
    public static void Canopy(this Area area, (double X, double Y, double R)[] lobes, Rgb[] ramp, int seed, bool bleed = false)
    {
        int w = area.Width, h = area.Height;
        int[,] owner = new int[w, h];
        double[,] lit = new double[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                owner[x, y] = -1;
                for (int i = 0; i < lobes.Length; i++)
                {
                    double dx = x - lobes[i].X, dy = y - lobes[i].Y;
                    double d2 = (dx * dx) + (dy * dy);
                    if (d2 <= lobes[i].R * lobes[i].R)
                    {
                        owner[x, y] = i;
                        double dist = Math.Sqrt(d2) / lobes[i].R;
                        // Light from the top left: the dot of the offset with the light direction, pushed to the lobe's rim.
                        lit[x, y] = (-((dx * 0.7) + (dy * 0.9)) / lobes[i].R) - (0.25 * dist * dist);
                    }
                }
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = owner[x, y];
                if (o < 0)
                {
                    continue;
                }

                bool silhouette = Edge(owner, x, y, w, h, -1, false, bleed);
                if (silhouette)
                {
                    area.Put(x, y, Palette.Outline);
                    continue;
                }

                bool overlap = Edge(owner, x, y, w, h, o, true);
                double l = lit[x, y] + (((int)(Noise.Of(x, y, seed) % 9) - 4) * 0.045);
                int tone = l > 0.62 ? 4 : l > 0.28 ? 3 : l > -0.12 ? 2 : l > -0.45 ? 1 : 0;
                if (overlap)
                {
                    tone = Math.Max(0, tone - 2);
                }

                // Leaf clusters: short bright flecks on the lit half, dark flecks on the shaded half.
                uint n = Noise.Of(x, y, seed + 3);
                if (n % 11 == 0 && tone >= 2)
                {
                    tone = Math.Min(4, tone + 1);
                }
                else if (n % 13 == 0 && tone >= 1)
                {
                    tone--;
                }

                area.Put(x, y, ramp[tone]);
            }
        }
    }

    private static int Lattice(int gx, int gy, int seed) => (int)(Noise.Of(gx, gy, seed) & 255);

    // Whether a neighbour of the pixel (four directions) is outside the shape (owner < 0) when <paramref name="own"/> is -1, or belongs to
    // a lobe drawn later than <paramref name="own"/> when overlap is asked.
    private static bool Edge(int[,] owner, int x, int y, int w, int h, int own, bool overlap = false, bool bleed = false)
    {
        ReadOnlySpan<(int Dx, int Dy)> steps = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        foreach ((int dx, int dy) in steps)
        {
            int nx = x + dx, ny = y + dy;
            if (bleed && ((uint)nx >= (uint)w || (uint)ny >= (uint)h) && ny >= 0 && ny < h)
            {
                continue;
            }

            int o = (uint)nx >= (uint)w || (uint)ny >= (uint)h ? -1 : owner[nx, ny];
            if (!overlap && o < 0)
            {
                return true;
            }

            if (overlap && o > own)
            {
                return true;
            }
        }

        return false;
    }
}
