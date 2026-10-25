// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class WalkerTests
{
    private static WalkGrid Grid()
    {
        // 5 x 3 tiles of 16 px; a wall at tile (3, 1).
        bool[] blocked = new bool[15];
        blocked[(1 * 5) + 3] = true;
        return new WalkGrid(5, 3, 16, blocked);
    }

    [Fact]
    public void Step_RightForOneSecond_MovesAtSpeedAndFacesRight()
    {
        Walker walker = new(8, 30);

        walker.Step(InputState.Right, 0.25, Grid());

        walker.X.Should().BeApproximately(8 + (Walker.Speed * 0.25), 1e-9);
        walker.Facing.Should().Be(Facing.Right);
        walker.IsMoving.Should().BeTrue();
    }

    [Fact]
    public void Step_IntoWall_StopsBeforeIt()
    {
        Walker walker = new(40, 30);

        for (int i = 0; i < 60; i++)
        {
            walker.Step(InputState.Right, 1.0 / 60, Grid());
        }

        (walker.X + (Walker.FootWidth / 2)).Should().BeLessThanOrEqualTo(48);
    }

    [Fact]
    public void Step_Diagonal_IsNotFasterThanStraight()
    {
        Walker walker = new(24, 24);

        walker.Step(InputState.Right | InputState.Down, 0.1, new WalkGrid(5, 3, 16, new bool[15]));

        double moved = System.Math.Sqrt(((walker.X - 24) * (walker.X - 24)) + ((walker.Y - 24) * (walker.Y - 24)));
        moved.Should().BeApproximately(Walker.Speed * 0.1, 1e-9);
    }

    [Fact]
    public void Step_NoInput_DoesNotMoveOrChangeFacing()
    {
        Walker walker = new(24, 24);

        walker.Step(InputState.None, 1, Grid());

        walker.X.Should().Be(24);
        walker.Facing.Should().Be(Facing.Down);
        walker.IsMoving.Should().BeFalse();
    }

    [Fact]
    public void IsBlocked_OutsideMap_IsTrue()
    {
        Grid().IsBlocked(-1, 0).Should().BeTrue();
        Grid().IsBlocked(5, 0).Should().BeTrue();
    }
}
