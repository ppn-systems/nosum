// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class TillGridTests
{
    // Tiles (local id = gid - 1): 0 grass (diggable), 1 stone (not diggable), 2 water.
    private static TileMap Map(int[] back, int[] buildings)
    {
        Dictionary<int, KeyValuePair<string, string>[]> props = new()
        {
            [0] = [new("Diggable", "T")],
            [2] = [new("Water", "T"), new("Diggable", "T")],
        };
        TileSet set = new(1, "t", "t", 4, 4, new TileProperties(4, props, new Dictionary<int, TileAnimation>()));
        return new TileMap(4, 1, 16, 16, [set], [new TileLayer("Back", back), new TileLayer("Buildings", buildings)]);
    }

    [Fact]
    public void FromMap_DiggableGround_IsTillable()
    {
        TileMap map = Map([1, 2, 1, 1], [0, 0, 0, 0]);

        TillGrid grid = TillGrid.FromMap(map, WalkGrid.FromMap(map));

        grid.IsTillable(0, 0).Should().BeTrue();
        grid.IsTillable(1, 0).Should().BeFalse();
    }

    [Fact]
    public void FromMap_BlockedTiles_AreNotTillable()
    {
        TileMap map = Map([1, 1, 3, 1], [0, 1, 0, 0]);

        TillGrid grid = TillGrid.FromMap(map, WalkGrid.FromMap(map));

        grid.IsTillable(0, 0).Should().BeTrue();
        grid.IsTillable(1, 0).Should().BeFalse("a building stands there");
        grid.IsTillable(2, 0).Should().BeFalse("it is water");
    }

    [Fact]
    public void IsTillable_OutsideTheMap_IsFalse()
    {
        TileMap map = Map([1, 1, 1, 1], [0, 0, 0, 0]);
        TillGrid grid = TillGrid.FromMap(map, WalkGrid.FromMap(map));

        grid.IsTillable(-1, 0).Should().BeFalse();
        grid.IsTillable(4, 0).Should().BeFalse();
        grid.Contains(4, 0).Should().BeFalse();
    }

    [Fact]
    public void FromMap_RealFarmMap_HasTillableGroundNearTheFarmhouseDoor()
    {
        TileMap map = TmxReader.Parse(File.ReadAllText(FindUp("art/own/Maps/Farm.tmx")));
        WalkGrid walk = FarmProps.BuildWalkGrid(map);

        TillGrid grid = TillGrid.FromMap(map, walk);

        int tillable = 0;
        (int doorX, int doorY) = FarmProps.FarmhouseDoorFront;
        for (int y = doorY - 6; y <= doorY + 8; y++)
        {
            for (int x = doorX - 8; x <= doorX + 8; x++)
            {
                if (grid.IsTillable(x, y))
                {
                    tillable++;
                }
            }
        }

        tillable.Should().BeGreaterThan(20);
        grid.IsTillable(doorX - 1, doorY - 1).Should().BeFalse("the farmhouse stands there");
    }

    private static string FindUp(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
