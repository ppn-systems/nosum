// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// The authority's check of a position a client claims. A client may walk at most
/// <see cref="Walker.Speed"/> pixels per second on average: time earns a movement budget (capped
/// at <see cref="MaxBudgetSeconds"/> of walking, so network jitter that bunches claims together
/// is tolerated but hoarding is not) and each accepted claim spends the distance it covers.
/// Pure: time is a parameter and nothing is read from a clock.
/// </summary>
public static class MoveValidator
{
    /// <summary>Longest stretch of walking, in seconds, that the budget can store (at normal speed).</summary>
    public const double MaxBudgetSeconds = 0.5;

    /// <summary>Extra pixels allowed on top of the budget for rounding and the client's fixed step.</summary>
    public const double SlackPixels = 2;

    private const double SweepSpacing = 2;

    /// <summary>
    /// Checks a claimed position and, when legal, applies it to <paramref name="state"/>.
    /// The budget earns time even when the claim is refused.
    /// </summary>
    /// <param name="state">The player's authoritative state.</param>
    /// <param name="x">Claimed feet x, world pixels.</param>
    /// <param name="y">Claimed feet y, world pixels.</param>
    /// <param name="facing">Claimed facing, 0 to 3.</param>
    /// <param name="moving">Claimed walking flag.</param>
    /// <param name="nowSeconds">Current time, seconds, on the clock used for <see cref="MotionState"/>.</param>
    /// <param name="grid">Walkable tiles of the map.</param>
    /// <param name="speedFactor">Movement speed multiplier of the player's active speed buffs (1 for none); the budget scales with it.</param>
    /// <returns>The verdict; the state changes only for <see cref="MoveVerdict.Accepted"/>.</returns>
    public static MoveVerdict Validate(ref MotionState state, double x, double y, int facing, bool moving, double nowSeconds, WalkGrid grid, double speedFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(speedFactor, 1);

        double speed = Walker.Speed * speedFactor;
        double elapsed = Math.Max(0, nowSeconds - state.LastSeconds);
        state.LastSeconds = nowSeconds;
        state.Budget = Math.Min(MaxBudgetSeconds * speed, state.Budget + (speed * elapsed));

        if (!double.IsFinite(x) || !double.IsFinite(y) || facing is < 0 or > 3)
        {
            return MoveVerdict.Invalid;
        }

        if (x < 0 || y < 0 || x >= grid.Width * grid.TileSize || y >= grid.Height * grid.TileSize)
        {
            return MoveVerdict.OutOfMap;
        }

        double distance = Math.Sqrt(((x - state.X) * (x - state.X)) + ((y - state.Y) * (y - state.Y)));
        if (distance > state.Budget + SlackPixels)
        {
            return MoveVerdict.TooFast;
        }

        if (FootBlocked(grid, x, y) || !PathClear(grid, state.X, state.Y, x, y))
        {
            return MoveVerdict.Blocked;
        }

        state.Budget = Math.Max(0, state.Budget - distance);
        state.X = x;
        state.Y = y;
        state.Facing = facing;
        state.Moving = moving;
        return MoveVerdict.Accepted;
    }

    /// <summary>Returns whether a character's footprint standing at a point overlaps a blocked tile.</summary>
    /// <param name="grid">Walkable tiles.</param>
    /// <param name="x">Feet x.</param>
    /// <param name="y">Feet y.</param>
    /// <returns><see langword="true"/> when blocked.</returns>
    public static bool FootBlocked(WalkGrid grid, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return grid.Overlaps(x - (Walker.FootWidth / 2), y - Walker.FootHeight, Walker.FootWidth, Walker.FootHeight);
    }

    // A walker slides along walls one axis at a time, so its real path can bend. Accept the
    // straight line, either one-bend path, or a two-bend corner slide; a wall thinner than the step cannot be jumped.
    private static bool PathClear(WalkGrid grid, double x0, double y0, double x1, double y1)
        => LegClear(grid, x0, y0, x1, y1)
        || (LegClear(grid, x0, y0, x1, y0) && LegClear(grid, x1, y0, x1, y1))
        || (LegClear(grid, x0, y0, x0, y1) && LegClear(grid, x0, y1, x1, y1))
        || TwoBendClear(grid, x0, y0, x1, y1);

    private static bool TwoBendClear(WalkGrid grid, double x0, double y0, double x1, double y1)
    {
        for (int i = 1; i <= 7; i++)
        {
            double t = i * 0.125;
            double midX = x0 + ((x1 - x0) * t);
            if (LegClear(grid, x0, y0, midX, y0) && LegClear(grid, midX, y0, midX, y1) && LegClear(grid, midX, y1, x1, y1))
            {
                return true;
            }

            double midY = y0 + ((y1 - y0) * t);
            if (LegClear(grid, x0, y0, x0, midY) && LegClear(grid, x0, midY, x1, midY) && LegClear(grid, x1, midY, x1, y1))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LegClear(WalkGrid grid, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        int samples = (int)Math.Ceiling(Math.Sqrt((dx * dx) + (dy * dy)) / SweepSpacing);
        for (int i = 1; i <= samples; i++)
        {
            double t = i / (double)samples;
            if (FootBlocked(grid, x0 + (dx * t), y0 + (dy * t)))
            {
                return false;
            }
        }

        return true;
    }
}
