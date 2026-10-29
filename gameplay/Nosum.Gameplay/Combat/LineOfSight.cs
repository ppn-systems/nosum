// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Tile line-of-sight for attacks: a strike needs a clear line between attacker and target, so solid walls block it
/// (issue #148).
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// Walks the tiles on the Bresenham line from one tile to another and reports whether any tile strictly between them is solid.
    /// The endpoints are not tested: the attacker stands on its own tile and the target on its own.
    /// </summary>
    /// <param name="grid">Walkable tiles of the map.</param>
    /// <param name="x0">Attacker tile X.</param>
    /// <param name="y0">Attacker tile Y.</param>
    /// <param name="x1">Target tile X.</param>
    /// <param name="y1">Target tile Y.</param>
    /// <returns><see langword="true"/> when no solid tile lies between the two tiles.</returns>
    public static bool IsClear(WalkGrid grid, int x0, int y0, int x1, int y1)
    {
        ArgumentNullException.ThrowIfNull(grid);

        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int x = x0;
        int y = y0;
        while (true)
        {
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y += sy;
            }

            if (x == x1 && y == y1)
            {
                return true;
            }

            if (grid.IsBlocked(x, y))
            {
                return false;
            }
        }
    }
}
