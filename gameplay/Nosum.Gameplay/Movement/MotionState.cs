// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Movement;

/// <summary>
/// What the authority knows about one walking player: the last accepted position plus the
/// movement budget <see cref="MoveValidator"/> spends.
/// </summary>
public struct MotionState
{
    /// <summary>Initializes a player standing still.</summary>
    /// <param name="x">Feet x, world pixels.</param>
    /// <param name="y">Feet y, world pixels.</param>
    /// <param name="nowSeconds">Current time in seconds on any monotonic clock; later calls use the same clock.</param>
    public MotionState(double x, double y, double nowSeconds)
    {
        this.X = x;
        this.Y = y;
        this.Facing = 0;
        this.Moving = false;
        this.Budget = MoveValidator.MaxBudgetSeconds * Walker.Speed;
        this.LastSeconds = nowSeconds;
    }

    /// <summary>Gets the last accepted feet x, world pixels.</summary>
    public double X { get; internal set; }

    /// <summary>Gets the last accepted feet y, world pixels.</summary>
    public double Y { get; internal set; }

    /// <summary>Gets the last accepted facing (0 down, 1 right, 2 up, 3 left).</summary>
    public int Facing { get; internal set; }

    /// <summary>Gets whether the player was walking at the last accepted claim.</summary>
    public bool Moving { get; internal set; }

    /// <summary>Gets the pixels of walking the player may still claim right now.</summary>
    public double Budget { get; internal set; }

    /// <summary>Gets the time of the last check, seconds.</summary>
    public double LastSeconds { get; internal set; }
}
