// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

// The turn a drag makes before its swing (docs/adr/ADR-074 D7): the farmer faces the side of the tile it works.
public sealed class WalkerFacingTests
{
    private const int TileSize = 16;

    // Feet at the centre-bottom of tile (5, 5), so the farmer stands on tile (5, 5).
    private const double FeetX = (5 * TileSize) + 8;
    private const double FeetY = (5 * TileSize) + 14;

    [Theory]
    [InlineData(6, 5, Facing.Right)]
    [InlineData(4, 5, Facing.Left)]
    [InlineData(5, 6, Facing.Down)]
    [InlineData(5, 4, Facing.Up)]
    public void TryFacingToward_OrthogonalNeighbour_FacesIt(int tileX, int tileY, Facing expected)
    {
        Walker.TryFacingToward(FeetX, FeetY, TileSize, tileX, tileY, out Facing facing).Should().BeTrue();
        facing.Should().Be(expected);
    }

    [Theory]
    [InlineData(7, 6, Facing.Right)]
    [InlineData(3, 4, Facing.Left)]
    [InlineData(6, 7, Facing.Down)]
    [InlineData(4, 3, Facing.Up)]
    public void TryFacingToward_FurtherOnOneAxis_FacesTheLongerAxis(int tileX, int tileY, Facing expected)
    {
        // The axis the tile lies further on wins: two tiles across beat one tile down, so the farmer faces the side.
        Walker.TryFacingToward(FeetX, FeetY, TileSize, tileX, tileY, out Facing facing).Should().BeTrue();
        facing.Should().Be(expected);
    }

    [Fact]
    public void TryFacingToward_DiagonalTie_FacesHorizontally()
    {
        Walker.TryFacingToward(FeetX, FeetY, TileSize, 6, 6, out Facing facing).Should().BeTrue();
        facing.Should().Be(Facing.Right, "a tie on the axes turns the farmer sideways");
    }

    [Fact]
    public void TryFacingToward_TheTileUnderTheFarmer_KeepsTheFacing()
    {
        Walker.TryFacingToward(FeetX, FeetY, TileSize, 5, 5, out _).Should().BeFalse("no side to face");
    }

    [Fact]
    public void TryFacingToward_FeetOnTheTileBorder_UsesTheTileTheFeetAreIn()
    {
        // The feet at the very edge of tile 5 still belong to it (floor of the coordinate), like the server's own tile.
        Walker.TryFacingToward(5 * TileSize, 5 * TileSize, TileSize, 4, 5, out Facing facing).Should().BeTrue();
        facing.Should().Be(Facing.Left);
    }

    [Fact]
    public void Face_TurnsWithoutMoving()
    {
        Walker walker = new(FeetX, FeetY, Facing.Left);

        walker.Face(Facing.Right);

        walker.Facing.Should().Be(Facing.Right);
        walker.X.Should().Be(FeetX);
        walker.Y.Should().Be(FeetY);
    }
}
