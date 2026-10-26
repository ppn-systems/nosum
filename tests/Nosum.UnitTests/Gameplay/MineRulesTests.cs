// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The mine (docs/adr/ADR-026 D3): its rules, its floor plans and the floors the planner builds on them.</summary>
public sealed class MineRulesTests
{
    private static readonly Guid s_ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (MapSpec Spec, WalkGrid Grid, HashSet<(int X, int Y)> Reserved, int SpawnX, int SpawnY) Floor(int floor)
    {
        _ = GameMaps.TryGet(MineRules.FloorMapId(floor), out MapSpec? spec);
        TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", spec!.TmxFile)));
        WalkGrid grid = spec.BuildWalkGrid(map);
        (double x, double y) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);
        int tx = WarpRule.TileX(x, grid.TileSize);
        int ty = WarpRule.TileY(y, grid.TileSize);

        // What the server reserves for a floor: the arrival tile and the tile north of it.
        return (spec, grid, [(tx, ty), (tx, ty - 1)], tx, ty);
    }

    public static TheoryData<int> Floors()
    {
        TheoryData<int> floors = [];
        for (int floor = 1; floor <= MineRules.MaxFloor; floor++)
        {
            floors.Add(floor);
        }

        return floors;
    }

    [Fact]
    public void Constants_AreFortyFloorsWithAnElevatorEveryFive()
    {
        MineRules.MaxFloor.Should().Be(40);
        Enumerable.Range(1, 40).Where(MineRules.HasElevator).Should().Equal(5, 10, 15, 20, 25, 30, 35, 40);
        MineRules.HasElevator(0).Should().BeFalse();
        MineRules.HasElevator(45).Should().BeFalse();
    }

    [Theory]
    [InlineData(1, true, false, true)]
    [InlineData(19, true, false, true)]
    [InlineData(20, true, true, true)]
    [InlineData(39, true, true, true)]
    [InlineData(40, false, true, false)]
    [InlineData(0, false, false, false)]
    public void OreAndLadders_FollowTheFloor(int floor, bool copper, bool iron, bool ladder)
    {
        MineRules.HasCopper(floor).Should().Be(copper);
        MineRules.HasIron(floor).Should().Be(iron);
        MineRules.HasLadder(floor).Should().Be(ladder);
    }

    [Theory]
    [InlineData(0, new[] { 0 })]
    [InlineData(4, new[] { 0 })]
    [InlineData(5, new[] { 0, 5 })]
    [InlineData(12, new[] { 0, 5, 10 })]
    [InlineData(40, new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40 })]
    [InlineData(99, new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40 })]
    public void ElevatorStops_AreTheEntranceAndEveryFifthFloorDownToTheDeepestReached(int deepest, int[] stops)
    {
        MineRules.ElevatorStops(deepest).Should().Equal(stops);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 5, false)]
    [InlineData(5, 5, true)]
    [InlineData(7, 5, true)]
    [InlineData(7, 10, false)]
    [InlineData(40, 40, true)]
    [InlineData(40, 45, false)]
    [InlineData(40, 7, false)]
    [InlineData(40, -5, false)]
    public void CanRide_OnlyToTheEntranceOrAnElevatorFloorTheyHaveBeenTo(int deepest, int target, bool allowed)
    {
        MineRules.CanRide(deepest, target).Should().Be(allowed);
    }

    [Fact]
    public void FloorSeed_DependsOnThePlayerTheDayAndTheFloor_AndNothingElse()
    {
        MineRules.FloorSeed(s_ada, 5, 3).Should().Be(MineRules.FloorSeed(s_ada, 5, 3));
        MineRules.FloorSeed(s_ada, 5, 3).Should().NotBe(MineRules.FloorSeed(s_bob, 5, 3));
        MineRules.FloorSeed(s_ada, 5, 3).Should().NotBe(MineRules.FloorSeed(s_ada, 6, 3));
        MineRules.FloorSeed(s_ada, 5, 3).Should().NotBe(MineRules.FloorSeed(s_ada, 5, 4));
    }

    [Fact]
    public void Layouts_AreFortyDistinctFilesThatExist()
    {
        MineLayouts.All.Length.Should().Be(MineRules.MaxFloor);
        MineLayouts.All.ToArray().Select(l => l.File).Should().OnlyHaveUniqueItems();
        for (int floor = 1; floor <= MineRules.MaxFloor; floor++)
        {
            File.Exists(RepoFiles.Path("art", "own", "Maps", MineLayouts.FileOf(floor))).Should().BeTrue($"floor {floor}");
        }
    }

    [Theory]
    [MemberData(nameof(Floors))]
    public void EveryFloor_ArrivalIsWalkable_AndInACaveOfUsefulSize(int floor)
    {
        (_, WalkGrid grid, _, int x, int y) = Floor(floor);

        grid.IsBlocked(x, y).Should().BeFalse();
        Reachable(grid, x, y).Count(r => r).Should().BeGreaterThan(80, "a floor needs room for stones and ore");
    }

    [Theory]
    [MemberData(nameof(Floors))]
    public void Plan_EveryFloor_HasStonesOreAndOneHiddenLadder_OnlyOnTilesAPlayerCanReach(int floor)
    {
        (_, WalkGrid grid, HashSet<(int X, int Y)> reserved, int sx, int sy) = Floor(floor);
        bool[] reachable = Reachable(grid, sx, sy);

        MineFloorPlanner.Plan plan = MineFloorPlanner.Build(floor, MineRules.FloorSeed(s_ada, 3, floor), grid, reserved, sx, sy);

        plan.Nodes.Should().NotBeEmpty().And.HaveCountLessThan(NodeLimits.MaxPerInstance);
        plan.Nodes.Select(n => (n.X, n.Y)).Should().OnlyHaveUniqueItems("one node per tile");
        plan.Nodes.Should().OnlyContain(n => reachable[(n.Y * grid.Width) + n.X], "every node can be walked up to");
        foreach (NodePlacement node in plan.Nodes)
        {
            reserved.Contains((node.X, node.Y)).Should().BeFalse();
            Math.Max(Math.Abs(node.X - sx), Math.Abs(node.Y - sy)).Should().BeGreaterThan(2, "the arrival is never walled in");
        }

        plan.Nodes.Count(n => n.Kind == NodeKind.Stone).Should().BeGreaterThanOrEqualTo(6);
        plan.Nodes.Any(n => n.Kind == NodeKind.CopperOre).Should().Be(MineRules.HasCopper(floor));
        plan.Nodes.Any(n => n.Kind == NodeKind.IronOre).Should().Be(MineRules.HasIron(floor));
        plan.Nodes.Where(n => n.Kind is not (NodeKind.Stone or NodeKind.CopperOre or NodeKind.IronOre)).Should().BeEmpty();

        List<NodePlacement> hiding = [.. plan.Nodes.Where(n => n.Variant == NodeVariants.LadderStone)];
        if (MineRules.HasLadder(floor))
        {
            hiding.Should().ContainSingle("one ladder per floor");
            hiding[0].Kind.Should().Be(NodeKind.Stone, "the ladder hides under a stone");
            plan.Ladder.Should().Be((hiding[0].X, hiding[0].Y));
        }
        else
        {
            hiding.Should().BeEmpty("the last floor has no ladder down");
            plan.Ladder.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(25)]
    [InlineData(40)]
    public void Plan_TheSameSeed_GivesTheSameFloor_AnotherPlayerOrDayGivesAnother(int floor)
    {
        (_, WalkGrid grid, HashSet<(int X, int Y)> reserved, int sx, int sy) = Floor(floor);

        MineFloorPlanner.Plan first = MineFloorPlanner.Build(floor, MineRules.FloorSeed(s_ada, 3, floor), grid, reserved, sx, sy);
        MineFloorPlanner.Plan again = MineFloorPlanner.Build(floor, MineRules.FloorSeed(s_ada, 3, floor), grid, reserved, sx, sy);
        MineFloorPlanner.Plan nextDay = MineFloorPlanner.Build(floor, MineRules.FloorSeed(s_ada, 4, floor), grid, reserved, sx, sy);
        MineFloorPlanner.Plan otherPlayer = MineFloorPlanner.Build(floor, MineRules.FloorSeed(s_bob, 3, floor), grid, reserved, sx, sy);

        again.Nodes.Should().Equal(first.Nodes);
        again.Ladder.Should().Be(first.Ladder);
        nextDay.Nodes.Should().NotEqual(first.Nodes, "the floor is rebuilt every game day");
        otherPlayer.Nodes.Should().NotEqual(first.Nodes, "nobody shares a floor");
    }

    [Fact]
    public void Plan_TheLadder_IsAmongTheFarthestStonesFromTheArrival()
    {
        (_, WalkGrid grid, HashSet<(int X, int Y)> reserved, int sx, int sy) = Floor(9);
        int[] farthestLadders = [.. Enumerable.Range(0, 30).Select(day =>
        {
            MineFloorPlanner.Plan plan = MineFloorPlanner.Build(9, MineRules.FloorSeed(s_ada, day, 9), grid, reserved, sx, sy);
            (int x, int y) = plan.Ladder!.Value;
            return Math.Abs(x - sx) + Math.Abs(y - sy);
        })];

        farthestLadders.Average().Should().BeGreaterThan(10, "the ladder sends the player across the floor");
        farthestLadders.Min().Should().BeGreaterThan(3);
    }

    [Fact]
    public void Plan_Ore_ScalesWithDepth_IronOnlyFromFloorTwenty()
    {
        (_, WalkGrid shallowGrid, HashSet<(int X, int Y)> shallowReserved, int shallowX, int shallowY) = Floor(10);
        (_, WalkGrid deepGrid, HashSet<(int X, int Y)> deepReserved, int deepX, int deepY) = Floor(30);

        MineFloorPlanner.Plan shallow = MineFloorPlanner.Build(10, 1, shallowGrid, shallowReserved, shallowX, shallowY);
        MineFloorPlanner.Plan deep = MineFloorPlanner.Build(30, 1, deepGrid, deepReserved, deepX, deepY);

        shallow.Nodes.Count(n => n.Kind == NodeKind.IronOre).Should().Be(0);
        deep.Nodes.Count(n => n.Kind == NodeKind.IronOre).Should().BeGreaterThan(1);
        deep.Nodes.Count(n => n.Kind == NodeKind.CopperOre).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Build_ABlockedArrival_PlansNothing()
    {
        WalkGrid grid = new(4, 4, 16, new bool[16]);
        grid.Block(1, 1, 1, 1);

        MineFloorPlanner.Build(1, 1, grid, new HashSet<(int X, int Y)>(), 1, 1).Nodes.Should().BeEmpty();
    }

    internal static bool[] Reachable(WalkGrid grid, int startX, int startY)
    {
        bool[] seen = new bool[grid.Width * grid.Height];
        if (grid.IsBlocked(startX, startY))
        {
            return seen;
        }

        Queue<(int X, int Y)> queue = new();
        queue.Enqueue((startX, startY));
        seen[(startY * grid.Width) + startX] = true;
        (int Dx, int Dy)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in steps)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height || seen[(ny * grid.Width) + nx] || grid.IsBlocked(nx, ny))
                {
                    continue;
                }

                seen[(ny * grid.Width) + nx] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return seen;
    }
}
