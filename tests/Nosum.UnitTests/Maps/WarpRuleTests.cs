// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Maps;

public sealed class WarpRuleTests
{
    private const int Down = 0;
    private const int Right = 1;
    private const int Up = 2;
    private const int Left = 3;

    // 10 x 8 tiles of 16 px, open ground.
    private static WalkGrid Grid() => new(10, 8, 16, new bool[10 * 8]);

    private static readonly MapWarp s_door = new(4, 3, "inside", 1, 1);
    private static readonly MapWarp s_east = new(10, 5, "east", 0, 5);
    private static readonly MapWarp s_north = new(2, -1, "north", 2, 7);

    private static readonly MapWarp[] s_warps = [s_door, s_east, s_north];

    // Feet of a character standing at the bottom centre of a tile.
    private static (double X, double Y) Feet(int tx, int ty) => ((tx + 0.5) * 16, ((ty + 1) * 16) - 1);

    [Fact]
    public void TryFind_StandingOnTheDoorTile_FindsIt()
    {
        (double x, double y) = Feet(4, 3);

        WarpRule.TryFind(s_warps, Grid(), x, y, Down, false, out MapWarp warp).Should().BeTrue();

        warp.Should().Be(s_door);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    [InlineData(4, 2)]
    [InlineData(4, 4)]
    public void TryFind_NextToTheDoorTile_FindsNothing(int tx, int ty)
    {
        (double x, double y) = Feet(tx, ty);

        WarpRule.TryFind(s_warps, Grid(), x, y, Down, true, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFind_FootJustInsideTheDoorTile_StillCountsAsStandingOnIt()
    {
        // 1 px inside the tile's left and top edges.
        WarpRule.TryFind(s_warps, Grid(), (4 * 16) + 1, (3 * 16) + 1, Down, false, out _).Should().BeTrue();

        // The feet box ends at y (exclusive): y exactly on the tile's top edge is the row above.
        WarpRule.TryFind(s_warps, Grid(), (4 * 16) + 8, 3 * 16, Down, false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFind_EdgeExitWalkingOutwards_IsUsed()
    {
        (double x, double y) = Feet(9, 5);

        WarpRule.TryFind(s_warps, Grid(), x, y, Right, true, out MapWarp warp).Should().BeTrue();

        warp.Should().Be(s_east);
    }

    [Theory]
    [InlineData(Left, true)]
    [InlineData(Up, true)]
    [InlineData(Right, false)]
    public void TryFind_EdgeExit_NeedsWalkingOutwards(int facing, bool moving)
    {
        (double x, double y) = Feet(9, 5);

        WarpRule.TryFind(s_warps, Grid(), x, y, facing, moving, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFind_TopEdgeExit_UsesTheFirstRowFacingUp()
    {
        (double x, double y) = Feet(2, 0);

        WarpRule.TryFind(s_warps, Grid(), x, y, Up, true, out MapWarp warp).Should().BeTrue();

        warp.Should().Be(s_north);
    }

    [Fact]
    public void TryFind_BottomEdge_FeetAtTheMapEndStayOnTheLastRow()
    {
        MapWarp south = new(3, 8, "south", 0, 0);

        WarpRule.TryFind([south], Grid(), (3 * 16) + 8, 8 * 16, Down, true, out _).Should().BeTrue();
    }

    [Fact]
    public void TryFind_ExitSourceTwoTilesOutside_IsNeverUsed()
    {
        WarpRule.TryFind([new MapWarp(12, 3, "far", 0, 0)], Grid(), 9.5 * 16, 4 * 16, Right, true, out _).Should().BeFalse();
    }

    [Fact]
    public void IsOnExitTile_DoorAndEdgeTiles_AreExitTiles()
    {
        WalkGrid grid = Grid();
        (double dx, double dy) = Feet(4, 3);
        (double ex, double ey) = Feet(9, 5);
        (double ox, double oy) = Feet(5, 5);

        WarpRule.IsOnExitTile(s_warps, grid, dx, dy).Should().BeTrue();
        WarpRule.IsOnExitTile(s_warps, grid, ex, ey).Should().BeTrue();
        WarpRule.IsOnExitTile(s_warps, grid, ox, oy).Should().BeFalse();
    }

    [Fact]
    public void Arrival_OpenTarget_StandsOnItFacingDown()
    {
        (double x, double y, int facing) = WarpRule.Arrival(Grid(), 4, 3);

        (x, y, facing).Should().Be((72d, 63d, Down));
    }

    [Fact]
    public void Arrival_OnAnEdgeTile_FacesAwayFromTheEdge()
    {
        WarpRule.Arrival(Grid(), 9, 4).Facing.Should().Be(Left);
        WarpRule.Arrival(Grid(), 0, 4).Facing.Should().Be(Right);
        WarpRule.Arrival(Grid(), 4, 7).Facing.Should().Be(Up);
        WarpRule.Arrival(Grid(), 4, 0).Facing.Should().Be(Down);
    }

    [Fact]
    public void Arrival_BlockedTarget_PrefersTheTileInFrontOfTheDoor()
    {
        WalkGrid grid = Grid();
        grid.Block(3, 1, 3, 3);

        (double x, double y, _) = WarpRule.Arrival(grid, 4, 3);

        (x, y).Should().Be((4.5 * 16, (5 * 16) - 1d));
    }
}
