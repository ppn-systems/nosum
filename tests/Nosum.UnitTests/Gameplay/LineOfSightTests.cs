// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Regression tests for issue #148: a solid wall between attacker and target blocks the strike.</summary>
public sealed class LineOfSightTests
{
    // 10 x 10 tiles; a solid wall on column 5, rows 0 to 8 (row 9 is a gap).
    private static WalkGrid WallGrid()
    {
        bool[] blocked = new bool[10 * 10];
        for (int row = 0; row < 9; row++)
        {
            blocked[(row * 10) + 5] = true;
        }

        return new WalkGrid(10, 10, 16, blocked);
    }

    [Fact]
    public void IsClear_TargetBehindWall_IsBlocked()
    {
        LineOfSight.IsClear(WallGrid(), 2, 4, 8, 4).Should().BeFalse();
    }

    [Fact]
    public void IsClear_TargetOnSameSideOfWall_IsClear()
    {
        LineOfSight.IsClear(WallGrid(), 1, 4, 3, 4).Should().BeTrue();
    }

    [Fact]
    public void IsClear_LineThroughTheGap_IsClear()
    {
        LineOfSight.IsClear(WallGrid(), 2, 9, 8, 9).Should().BeTrue();
    }

    [Fact]
    public void IsClear_LongLineWithVerticalRunPastTheWall_IsBlocked()
    {
        // Crosses the wall column at row 5, then runs down the open side: the wall check must not stop early.
        LineOfSight.IsClear(WallGrid(), 2, 5, 6, 0).Should().BeFalse();
    }

    [Fact]
    public void IsClear_DiagonalThroughWallCorner_IsBlockedWhenTheWallIsOnTheLine()
    {
        LineOfSight.IsClear(WallGrid(), 4, 3, 6, 3).Should().BeFalse();
    }

    [Fact]
    public void IsClear_AdjacentTargets_AreAlwaysClear()
    {
        LineOfSight.IsClear(WallGrid(), 3, 4, 4, 4).Should().BeTrue();
    }

    [Fact]
    public void IsClear_SameTile_IsClear()
    {
        // A monster on the player's own tile is in reach of a skill: nothing lies between a tile and itself.
        LineOfSight.IsClear(WallGrid(), 2, 4, 2, 4).Should().BeTrue();
    }

    [Fact]
    public void IsClear_ReversedDirection_MatchesForwardDirection()
    {
        WalkGrid grid = WallGrid();

        LineOfSight.IsClear(grid, 8, 4, 2, 4).Should().Be(LineOfSight.IsClear(grid, 2, 4, 8, 4));
    }
}
