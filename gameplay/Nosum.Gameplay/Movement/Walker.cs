// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// A character moving on a <see cref="WalkGrid"/>: position is the feet (bottom centre), the
/// collision footprint is a small box at the feet, not the sprite (docs/adr/ADR-005 D6).
/// </summary>
public struct Walker
{
    /// <summary>Walking speed in pixels per second.</summary>
    public const double Speed = 80;

    /// <summary>Footprint width in pixels.</summary>
    public const double FootWidth = 10;

    /// <summary>Footprint height in pixels.</summary>
    public const double FootHeight = 6;

    /// <summary>Initializes a walker standing at a point, facing down.</summary>
    /// <param name="x">Feet x, world pixels.</param>
    /// <param name="y">Feet y, world pixels.</param>
    public Walker(double x, double y)
        : this(x, y, Facing.Down)
    {
    }

    /// <summary>Initializes a walker standing at a point.</summary>
    /// <param name="x">Feet x, world pixels.</param>
    /// <param name="y">Feet y, world pixels.</param>
    /// <param name="facing">The direction the walker faces.</param>
    public Walker(double x, double y, Facing facing)
    {
        this.X = x;
        this.Y = y;
        this.Facing = facing;
    }

    /// <summary>Gets the feet x, world pixels.</summary>
    public double X { get; private set; }

    /// <summary>Gets the feet y, world pixels.</summary>
    public double Y { get; private set; }

    /// <summary>Gets the facing.</summary>
    public Facing Facing { get; private set; }

    /// <summary>Gets whether the walker moved during the last step.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// Advances one fixed step. Axes are resolved separately, so the walker slides along walls.
    /// </summary>
    /// <param name="input">Held directions.</param>
    /// <param name="seconds">Step length in seconds.</param>
    /// <param name="grid">Walkable tiles.</param>
    /// <param name="speedFactor">Speed multiplier of the active speed buffs (1 for none); the server validates with the same factor.</param>
    public void Step(InputState input, double seconds, WalkGrid grid, double speedFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentOutOfRangeException.ThrowIfLessThan(speedFactor, 1);

        int dx = (input.HasFlag(InputState.Right) ? 1 : 0) - (input.HasFlag(InputState.Left) ? 1 : 0);
        int dy = (input.HasFlag(InputState.Down) ? 1 : 0) - (input.HasFlag(InputState.Up) ? 1 : 0);
        this.IsMoving = dx != 0 || dy != 0;
        if (!this.IsMoving)
        {
            return;
        }

        this.Facing = dy > 0 ? Facing.Down : dy < 0 ? Facing.Up : dx > 0 ? Facing.Right : Facing.Left;
        double distance = Speed * speedFactor * seconds * (dx != 0 && dy != 0 ? Math.Sqrt(0.5) : 1);

        double nextX = this.X + (dx * distance);
        bool movedX = !Blocked(grid, nextX, this.Y);
        if (movedX)
        {
            this.X = nextX;
        }

        double nextY = this.Y + (dy * distance);
        bool movedY = !Blocked(grid, this.X, nextY);
        if (movedY)
        {
            this.Y = nextY;
        }

        if (dx != 0 && dy != 0)
        {
            bool atWestBorder = dx < 0 && this.X < grid.TileSize;
            bool atEastBorder = dx > 0 && this.X >= (grid.Width - 1) * grid.TileSize;
            if (atWestBorder || atEastBorder || (movedX && !movedY))
            {
                this.Facing = dx > 0 ? Facing.Right : Facing.Left;
            }
        }
    }

    private static bool Blocked(WalkGrid grid, double x, double y)
        => grid.Overlaps(x - (FootWidth / 2), y - FootHeight, FootWidth, FootHeight);
}
