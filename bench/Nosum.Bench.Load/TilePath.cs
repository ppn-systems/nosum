// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;

namespace Nosum.Bench.Load;

/// <summary>Finds a way across a map's tiles: breadth-first over the four neighbours of every unblocked tile.</summary>
internal static class TilePath
{
    /// <summary>Finds the shortest tile path.</summary>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="start">The tile to start on.</param>
    /// <param name="goal">The tile to reach; it counts as open even when the grid blocks it (a door).</param>
    /// <returns>The tiles to walk through after the start, ending with the goal; empty when already there; null when unreachable.</returns>
    public static List<(int X, int Y)>? Find(WalkGrid grid, (int X, int Y) start, (int X, int Y) goal)
        => FindNearest(grid, start, (x, y) => x == goal.X && y == goal.Y, goal);

    /// <summary>Finds the shortest tile path to the nearest tile that satisfies a condition.</summary>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="start">The tile to start on.</param>
    /// <param name="isGoal">Tells whether a tile is a wanted destination; the start itself never counts.</param>
    /// <param name="openGoal">A tile that counts as open even when the grid blocks it (a door), or null.</param>
    /// <returns>The tiles to walk through after the start, ending with the destination; null when none can be reached.</returns>
    public static List<(int X, int Y)>? FindNearest(WalkGrid grid, (int X, int Y) start, Func<int, int, bool> isGoal, (int X, int Y)? openGoal = null)
    {
        if (openGoal == start)
        {
            return [];
        }

        int width = grid.Width;
        int height = grid.Height;
        int[] previous = new int[width * height];
        System.Array.Fill(previous, -2);
        Queue<int> queue = new();
        int from = (start.Y * width) + start.X;
        if (start.X < 0 || start.Y < 0 || start.X >= width || start.Y >= height)
        {
            return null;
        }

        previous[from] = -1;
        queue.Enqueue(from);
        ReadOnlySpan<(int Dx, int Dy)> steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int cx = current % width;
            int cy = current / width;
            foreach ((int dx, int dy) in steps)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                {
                    continue;
                }

                int next = (ny * width) + nx;
                bool arrived = isGoal(nx, ny);
                bool open = openGoal is { } door && door.X == nx && door.Y == ny;
                if (previous[next] != -2 || (!open && grid.IsBlocked(nx, ny)))
                {
                    continue;
                }

                previous[next] = current;
                if (arrived)
                {
                    List<(int X, int Y)> path = [];
                    for (int at = next; at != from; at = previous[at])
                    {
                        path.Add((at % width, at / width));
                    }

                    path.Reverse();
                    return path;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }
}
