// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class WalkGridTests
{
    // Tiles (local id = gid - 1): 0 grass, 1 water, 2 impassable ground, 3 passable.
    private static TileMap Map(int[] back, int[] buildings)
    {
        Dictionary<int, KeyValuePair<string, string>[]> props = new()
        {
            [1] = [new("Water", "T")],
            [2] = [new("Passable", "F")],
            [3] = [new("Passable", "T")],
        };
        TileSet set = new(1, "t", "t", 4, 4, new TileProperties(4, props, new Dictionary<int, TileAnimation>()));
        return new TileMap(4, 1, 16, 16, [set], [new TileLayer("Back", back), new TileLayer("Buildings", buildings)]);
    }

    [Fact]
    public void FromMap_WaterOnBack_Blocks()
    {
        WalkGrid grid = WalkGrid.FromMap(Map([1, 2, 3, 4], [0, 0, 0, 0]));

        grid.IsBlocked(0, 0).Should().BeFalse();
        grid.IsBlocked(1, 0).Should().BeTrue();
        grid.IsBlocked(2, 0).Should().BeTrue();
        grid.IsBlocked(3, 0).Should().BeFalse();
    }

    [Fact]
    public void FromMap_BuildingsTile_BlocksUnlessPassable()
    {
        WalkGrid grid = WalkGrid.FromMap(Map([1, 1, 1, 1], [0, 1, 4, 3]));

        grid.IsBlocked(0, 0).Should().BeFalse();
        grid.IsBlocked(1, 0).Should().BeTrue();
        grid.IsBlocked(2, 0).Should().BeFalse();
        grid.IsBlocked(3, 0).Should().BeTrue();
    }

    [Fact]
    public void Step_TowardWater_StopsAtShore()
    {
        WalkGrid grid = WalkGrid.FromMap(Map([1, 2, 1, 1], [0, 0, 0, 0]));
        Walker walker = new(8, 15);

        for (int i = 0; i < 60; i++)
        {
            walker.Step(InputState.Right, 1.0 / 60, grid);
        }

        (walker.X + (Walker.FootWidth / 2)).Should().BeLessThanOrEqualTo(16);
    }

    [Fact]
    public void BlockFootprints_Prop_BlocksItsTilesOnly()
    {
        WalkGrid grid = new(6, 4, 16, new bool[24]);
        PropDefinition def = new("p", "s", 0, 0, 32, 48, 2, 1);

        grid.BlockFootprints([new PropPlacement(def, 3, 2)]);

        grid.IsBlocked(3, 2).Should().BeTrue();
        grid.IsBlocked(4, 2).Should().BeTrue();
        grid.IsBlocked(3, 1).Should().BeFalse();
        grid.IsBlocked(5, 2).Should().BeFalse();
    }
}
