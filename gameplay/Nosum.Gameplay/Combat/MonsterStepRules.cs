// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// One-tile monster steps: no diagonal corner clipping through wall corners, and no two living monsters on one tile
/// (issues #166).
/// </summary>
public static class MonsterStepRules
{
    /// <summary>
    /// Returns whether a monster may step from one tile to an adjacent one. A diagonal step needs both orthogonal
    /// corner tiles open, so a monster cannot slip through the vertex where two walls meet.
    /// </summary>
    /// <param name="grid">Walkable tiles of the map.</param>
    /// <param name="fromX">Start tile X.</param>
    /// <param name="fromY">Start tile Y.</param>
    /// <param name="toX">Target tile X.</param>
    /// <param name="toY">Target tile Y.</param>
    /// <returns><see langword="true"/> when the step is legal on the grid.</returns>
    public static bool CanStep(WalkGrid grid, int fromX, int fromY, int toX, int toY)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (grid.IsBlocked(toX, toY))
        {
            return false;
        }

        if (fromX != toX && fromY != toY)
        {
            return !grid.IsBlocked(toX, fromY) && !grid.IsBlocked(fromX, toY);
        }

        return true;
    }

    /// <summary>
    /// Picks a legal step toward a target tile: the diagonal first, then the horizontal, then the vertical axis. A tile
    /// held by another living monster is never chosen.
    /// </summary>
    /// <param name="grid">Walkable tiles of the map.</param>
    /// <param name="monsters">Monsters on the map instance; the stepping monster itself is recognised by <paramref name="monsterId"/>.</param>
    /// <param name="monsterId">Id of the monster that steps.</param>
    /// <param name="fromX">Its tile X.</param>
    /// <param name="fromY">Its tile Y.</param>
    /// <param name="targetX">Tile X it approaches.</param>
    /// <param name="targetY">Tile Y it approaches.</param>
    /// <param name="stepX">The chosen tile X when a step exists.</param>
    /// <param name="stepY">The chosen tile Y when a step exists.</param>
    /// <returns><see langword="true"/> when a step was chosen.</returns>
    public static bool TryStepToward(
        WalkGrid grid,
        IReadOnlyList<MonsterSpawn> monsters,
        int monsterId,
        int fromX,
        int fromY,
        int targetX,
        int targetY,
        out int stepX,
        out int stepY)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        int sx = Math.Sign(targetX - fromX);
        int sy = Math.Sign(targetY - fromY);
        Span<(int X, int Y)> candidates = stackalloc (int X, int Y)[3];
        int count = 0;
        if (sx != 0 && sy != 0)
        {
            candidates[count++] = (fromX + sx, fromY + sy);
        }

        if (sx != 0)
        {
            candidates[count++] = (fromX + sx, fromY);
        }

        if (sy != 0)
        {
            candidates[count++] = (fromX, fromY + sy);
        }

        for (int i = 0; i < count; i++)
        {
            (int x, int y) = candidates[i];
            if (CanStep(grid, fromX, fromY, x, y) && !IsOccupied(monsters, monsterId, x, y))
            {
                stepX = x;
                stepY = y;
                return true;
            }
        }

        stepX = fromX;
        stepY = fromY;
        return false;
    }

    /// <summary>
    /// Picks the one-tile knockback step for a monster struck from a tile: directly away from the attacker, on each axis the
    /// attacker is offset along. The step is refused when the grid blocks it (including a diagonal past a wall corner) or
    /// another living monster holds it (docs/adr/ADR-076).
    /// </summary>
    /// <param name="grid">Walkable tiles of the map.</param>
    /// <param name="monsters">Monsters on the map instance; the struck monster itself is recognised by <paramref name="monsterId"/>.</param>
    /// <param name="monsterId">Id of the struck monster.</param>
    /// <param name="x">Its tile X.</param>
    /// <param name="y">Its tile Y.</param>
    /// <param name="attackerX">Tile X of the attacker.</param>
    /// <param name="attackerY">Tile Y of the attacker.</param>
    /// <param name="knockedX">The tile X it is pushed to when a push exists.</param>
    /// <param name="knockedY">The tile Y it is pushed to when a push exists.</param>
    /// <returns><see langword="true"/> when the monster can be pushed one tile.</returns>
    public static bool TryKnockback(
        WalkGrid grid,
        IReadOnlyList<MonsterSpawn> monsters,
        int monsterId,
        int x,
        int y,
        int attackerX,
        int attackerY,
        out int knockedX,
        out int knockedY)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(monsters);

        int sx = Math.Sign(x - attackerX);
        int sy = Math.Sign(y - attackerY);
        int tx = x + sx;
        int ty = y + sy;
        if ((sx == 0 && sy == 0) || !CanStep(grid, x, y, tx, ty) || IsOccupied(monsters, monsterId, tx, ty))
        {
            knockedX = x;
            knockedY = y;
            return false;
        }

        knockedX = tx;
        knockedY = ty;
        return true;
    }

    /// <summary>Returns whether another living monster stands on a tile.</summary>
    /// <param name="monsters">Monsters on the map instance.</param>
    /// <param name="monsterId">The monster asking; it does not block itself.</param>
    /// <param name="x">Tile X.</param>
    /// <param name="y">Tile Y.</param>
    /// <returns><see langword="true"/> when occupied.</returns>
    public static bool IsOccupied(IReadOnlyList<MonsterSpawn> monsters, int monsterId, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(monsters);

        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterSpawn other = monsters[i];
            if (other.Hp > 0 && other.MonsterId != monsterId && other.X == x && other.Y == y)
            {
                return true;
            }
        }

        return false;
    }
}
