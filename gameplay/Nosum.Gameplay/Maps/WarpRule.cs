// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// When a player takes an exit and where they arrive. Pure: the server calls it with the position it
/// keeps, never with a client claim of "I am at the door".
/// </summary>
public static class WarpRule
{
    private const double FeetEpsilon = 0.001;

    /// <summary>Returns the tile column a feet position stands in.</summary>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns>The tile x.</returns>
    public static int TileX(double feetX, int tileSize) => (int)Math.Floor(feetX / tileSize);

    /// <summary>
    /// Returns the tile row a feet position stands in. The feet box ends at <c>y</c> (exclusive), as in
    /// <see cref="WalkGrid.Overlaps"/>, so a walker pressed against the bottom edge is still on the last row.
    /// </summary>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns>The tile y.</returns>
    public static int TileY(double feetY, int tileSize) => (int)Math.Floor((feetY - FeetEpsilon) / tileSize);

    /// <summary>
    /// Finds the exit a player is using. They must stand on the exit's tile. For an edge exit (source outside
    /// the map) they must stand on the nearest edge tile, be walking, and face outwards.
    /// </summary>
    /// <param name="warps">The map's exits.</param>
    /// <param name="grid">The map's walk grid (gives the map size and tile size).</param>
    /// <param name="feetX">Feet x the server keeps.</param>
    /// <param name="feetY">Feet y the server keeps.</param>
    /// <param name="facing">Facing, 0 down, 1 right, 2 up, 3 left.</param>
    /// <param name="moving">Whether the player is walking.</param>
    /// <param name="warp">The exit taken, when found.</param>
    /// <returns><see langword="true"/> when the player is using an exit.</returns>
    public static bool TryFind(IReadOnlyList<MapWarp> warps, WalkGrid grid, double feetX, double feetY, int facing, bool moving, out MapWarp warp)
    {
        ArgumentNullException.ThrowIfNull(warps);
        ArgumentNullException.ThrowIfNull(grid);

        int tx = TileX(feetX, grid.TileSize);
        int ty = TileY(feetY, grid.TileSize);
        for (int i = 0; i < warps.Count; i++)
        {
            MapWarp candidate = warps[i];
            if (Uses(candidate, grid, tx, ty, facing, moving))
            {
                warp = candidate;
                return true;
            }
        }

        warp = default;
        return false;
    }

    /// <summary>Returns whether a position stands on any exit tile (ignoring facing); a player who just arrived there must step off before an exit works again.</summary>
    /// <param name="warps">The map's exits.</param>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="feetX">Feet x.</param>
    /// <param name="feetY">Feet y.</param>
    /// <returns><see langword="true"/> when the position is on an exit tile.</returns>
    public static bool IsOnExitTile(IReadOnlyList<MapWarp> warps, WalkGrid grid, double feetX, double feetY)
    {
        ArgumentNullException.ThrowIfNull(warps);
        ArgumentNullException.ThrowIfNull(grid);

        int tx = TileX(feetX, grid.TileSize);
        int ty = TileY(feetY, grid.TileSize);
        for (int i = 0; i < warps.Count; i++)
        {
            if (!TryEdge(warps[i], grid, out int edgeX, out int edgeY, out _))
            {
                continue;
            }

            if (edgeX == tx && edgeY == ty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns where a player arrives: the walkable tile nearest the target (a tile south of it wins a tie, as doors open southwards),
    /// as feet position and facing. On a map edge the player faces away from it.
    /// </summary>
    /// <param name="grid">The destination map's walk grid.</param>
    /// <param name="targetX">Target tile x.</param>
    /// <param name="targetY">Target tile y.</param>
    /// <returns>Feet x, feet y and facing (0 down, 1 right, 2 up, 3 left).</returns>
    public static (double X, double Y, int Facing) Arrival(WalkGrid grid, int targetX, int targetY)
    {
        ArgumentNullException.ThrowIfNull(grid);

        (double x, double y) = SpawnPoint.FindSouthFirst(grid, targetX, targetY);
        int tx = TileX(x, grid.TileSize);
        int ty = TileY(y, grid.TileSize);
        int facing = tx == 0 ? 1 : tx == grid.Width - 1 ? 3 : ty == 0 ? 0 : ty == grid.Height - 1 ? 2 : 0;
        return (x, y, facing);
    }

    /// <summary>Resolves an exit's source to the tile a player stands on and, for an edge exit, the outward facing.</summary>
    /// <param name="warp">The exit.</param>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="tileX">The tile x to stand on.</param>
    /// <param name="tileY">The tile y to stand on.</param>
    /// <param name="outwardFacing">The facing that leaves the map, or -1 for an exit inside the map.</param>
    /// <returns><see langword="false"/> when the source is not on or one tile beside the map.</returns>
    public static bool TryEdge(MapWarp warp, WalkGrid grid, out int tileX, out int tileY, out int outwardFacing)
    {
        ArgumentNullException.ThrowIfNull(grid);

        tileX = warp.FromX;
        tileY = warp.FromY;
        outwardFacing = -1;
        bool outX = warp.FromX < 0 || warp.FromX >= grid.Width;
        bool outY = warp.FromY < 0 || warp.FromY >= grid.Height;
        if (outX && outY)
        {
            return false;
        }

        if (outX)
        {
            outwardFacing = warp.FromX < 0 ? 3 : 1;
            tileX = warp.FromX < 0 ? 0 : grid.Width - 1;
            return warp.FromX is -1 || warp.FromX == grid.Width;
        }

        if (outY)
        {
            outwardFacing = warp.FromY < 0 ? 2 : 0;
            tileY = warp.FromY < 0 ? 0 : grid.Height - 1;
            return warp.FromY is -1 || warp.FromY == grid.Height;
        }

        return true;
    }

    private static bool Uses(MapWarp warp, WalkGrid grid, int tx, int ty, int facing, bool moving)
    {
        if (!TryEdge(warp, grid, out int x, out int y, out int outward) || x != tx || y != ty)
        {
            return false;
        }

        return outward < 0 || (moving && facing == outward);
    }
}
