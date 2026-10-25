// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Movement;

/// <summary>Finds where a character may stand when entering a map.</summary>
public static class SpawnPoint
{
    /// <summary>
    /// Returns the feet position at the bottom centre of the walkable tile nearest to a preferred
    /// tile (same convention as <see cref="Walker"/>).
    /// </summary>
    /// <param name="grid">Walkable tiles.</param>
    /// <param name="tileX">Preferred tile x.</param>
    /// <param name="tileY">Preferred tile y.</param>
    /// <returns>Feet x and y in world pixels.</returns>
    public static (double X, double Y) Find(WalkGrid grid, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(grid);

        int x = tileX;
        int y = tileY;
        bool found = false;
        for (int radius = 0; radius < Math.Max(grid.Width, grid.Height) && !found; radius++)
        {
            for (int dy = -radius; dy <= radius && !found; dy++)
            {
                for (int dx = -radius; dx <= radius && !found; dx++)
                {
                    if (!grid.IsBlocked(tileX + dx, tileY + dy))
                    {
                        x = tileX + dx;
                        y = tileY + dy;
                        found = true;
                    }
                }
            }
        }

        return ((x + 0.5) * grid.TileSize, ((y + 1) * grid.TileSize) - 1);
    }
}
