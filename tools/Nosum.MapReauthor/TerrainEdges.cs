// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.MapReauthor;

/// <summary>
/// Chooses the corner tile of every cell where terrains meet. Corners are the grid's vertices: a vertex takes the kind of the highest-priority
/// cell around it (so the shore is drawn half a tile inside the lower terrain, never over it), and two kinds with no <see cref="WangSet"/> between
/// them meet through grass. A cell whose four corners are not all one kind gets the corner tile of the two kinds that meet in it.
/// </summary>
internal static class TerrainEdges
{
    /// <summary>Resolves the corner tile of every cell.</summary>
    /// <param name="kinds">The kind of each cell, row by row; <see cref="TerrainKind.Other"/> and kinds without art take no part.</param>
    /// <param name="width">Width in cells.</param>
    /// <param name="height">Height in cells.</param>
    /// <returns>Per cell the local tile index in the own tileset, or -1 where the cell needs no corner tile.</returns>
    public static int[] Resolve(TerrainKind[] kinds, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        TerrainKind[] vertices = new TerrainKind[(width + 1) * (height + 1)];
        for (int vy = 0; vy <= height; vy++)
        {
            for (int vx = 0; vx <= width; vx++)
            {
                vertices[(vy * (width + 1)) + vx] = Vertex(kinds, width, height, vx, vy);
            }
        }

        int[] result = new int[kinds.Length];
        Array.Fill(result, -1);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (TerrainKinds.Priority(kinds[(y * width) + x]) < 0)
                {
                    continue;
                }

                TerrainKind[] corners =
                [
                    vertices[(y * (width + 1)) + x],
                    vertices[(y * (width + 1)) + x + 1],
                    vertices[((y + 1) * (width + 1)) + x],
                    vertices[((y + 1) * (width + 1)) + x + 1],
                ];
                result[(y * width) + x] = Corner(corners);
            }
        }

        return result;
    }

    private static TerrainKind Vertex(TerrainKind[] kinds, int width, int height, int vx, int vy)
    {
        TerrainKind top = TerrainKind.Other;
        int present = 0;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int x = vx + dx, y = vy + dy;
                if (x < 0 || y < 0 || x >= width || y >= height)
                {
                    continue;
                }

                TerrainKind kind = kinds[(y * width) + x];
                if (TerrainKinds.Priority(kind) >= 0)
                {
                    present |= 1 << TerrainKinds.Priority(kind);
                    if (TerrainKinds.Priority(kind) > TerrainKinds.Priority(top))
                    {
                        top = kind;
                    }
                }
            }
        }

        // The highest kind wins when every other kind present has a set with it; otherwise they meet through grass.
        for (int p = 0; p < TerrainKinds.Fills.Length; p++)
        {
            if ((present & (1 << p)) != 0 && TerrainKinds.Fills[p] != top && WangSet.Find(TerrainKinds.Fills[p], top) < 0)
            {
                return TerrainKind.Grass;
            }
        }

        return top;
    }

    private static int Corner(TerrainKind[] corners)
    {
        while (true)
        {
            // The kinds present, lowest priority first. With more than two, the lowest is replaced by the next one up.
            List<TerrainKind> present = [];
            foreach (TerrainKind kind in TerrainKinds.Fills)
            {
                if (Array.IndexOf(corners, kind) >= 0)
                {
                    present.Add(kind);
                }
            }

            if (present.Count < 2)
            {
                return -1;
            }

            if (present.Count == 2)
            {
                int set = WangSet.Find(present[0], present[1]);
                return set < 0 ? -1 : OwnTileset.Corner(set, WangSet.All[set].Code(corners[0], corners[1], corners[2], corners[3]));
            }

            Replace(corners, present[0], present[1]);
        }
    }

    private static void Replace(TerrainKind[] corners, TerrainKind from, TerrainKind to)
    {
        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i] == from)
            {
                corners[i] = to;
            }
        }
    }
}
