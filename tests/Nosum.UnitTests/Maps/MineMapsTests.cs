// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.UnitTests.Gameplay;
using Xunit;

namespace Nosum.UnitTests.Maps;

/// <summary>The recorded tiles of the mine on the placeholder maps (docs/adr/ADR-007, ADR-026): Town's gate, the entrance cave, the shaft, the elevator and the floors' stairs.</summary>
public sealed class MineMapsTests
{
    private static (MapSpec Spec, TileMap Map, WalkGrid Grid) Load(string id)
    {
        _ = GameMaps.TryGet(id, out MapSpec? spec);
        TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("assets", "Maps", spec!.TmxFile)));
        return (spec, map, spec.BuildWalkGrid(map));
    }

    [Fact]
    public void TownGate_FiveEdgeTilesOnTheNorthRoad_AreWalkableAndReachableFromTheSpawn_AndLeadToTheCave()
    {
        (MapSpec town, TileMap map, WalkGrid grid) = Load(GameMaps.Town);
        bool[] reachable = MineRulesTests.Reachable(grid, town.SpawnTileX, town.SpawnTileY);

        List<MapWarp> toMine = [.. town.ResolveWarps(map).Where(w => w.TargetMap == GameMaps.Mine)];

        toMine.Select(w => w.FromX).Should().Equal(Enumerable.Range(MineWorld.TownGateFromX, 5));
        toMine.Should().OnlyContain(w => w.FromY == -1 && w.TargetX == MineWorld.CaveArrival.X && w.TargetY == MineWorld.CaveArrival.Y);
        foreach (MapWarp warp in toMine)
        {
            grid.IsBlocked(warp.FromX, 0).Should().BeFalse($"the edge tile of the gate at x = {warp.FromX}");
            reachable[warp.FromX].Should().BeTrue($"the gate at x = {warp.FromX} can be walked to");
        }

        grid.IsBlocked(MineWorld.TownArrival.X, MineWorld.TownArrival.Y).Should().BeFalse();
        town.ResolveWarps(map).Any(w => w.FromX == MineWorld.TownArrival.X && w.FromY == MineWorld.TownArrival.Y).Should().BeFalse("the arrival tile is not an exit, so nobody bounces back");
    }

    [Fact]
    public void Cave_ShaftElevatorAndWayBackToTown_AreReachableFromItsSpawn()
    {
        (MapSpec cave, TileMap map, WalkGrid grid) = Load(GameMaps.Mine);
        (double spawnX, double spawnY) = SpawnPoint.Find(grid, cave.SpawnTileX, cave.SpawnTileY);
        bool[] reachable = MineRulesTests.Reachable(grid, WarpRule.TileX(spawnX, grid.TileSize), WarpRule.TileY(spawnY, grid.TileSize));

        grid.IsBlocked(MineWorld.CaveShaft.X, MineWorld.CaveShaft.Y).Should().BeFalse("the shaft tile is opened so a farmer can step into it");
        reachable[(MineWorld.CaveShaft.Y * grid.Width) + MineWorld.CaveShaft.X].Should().BeTrue();
        reachable[(MineWorld.CaveShaftFront.Y * grid.Width) + MineWorld.CaveShaftFront.X].Should().BeTrue();
        reachable[(MineWorld.CaveTownExit.Y * grid.Width) + MineWorld.CaveTownExit.X].Should().BeTrue();
        reachable[(MineWorld.CaveArrival.Y * grid.Width) + MineWorld.CaveArrival.X].Should().BeTrue();

        // The elevator's own tile is part of the wall; the farmer stands in front of it.
        grid.IsBlocked(MineWorld.CaveElevator.X, MineWorld.CaveElevator.Y).Should().BeTrue();
        reachable[((MineWorld.CaveElevator.Y + 1) * grid.Width) + MineWorld.CaveElevator.X].Should().BeTrue();
        cave.Elevator.Should().Be(new ElevatorPoint(MineWorld.CaveElevator.X, MineWorld.CaveElevator.Y));

        List<MapWarp> warps = cave.ResolveWarps(map);
        warps.Should().Contain(new MapWarp(MineWorld.CaveShaft.X, MineWorld.CaveShaft.Y, "mine1", MineLayouts.For(1).SpawnX, MineLayouts.For(1).SpawnY));
        warps.Should().Contain(w => w.TargetMap == GameMaps.Town && w.FromX == MineWorld.CaveTownExit.X && w.FromY == MineWorld.CaveTownExit.Y);
        warps.Should().NotContain(w => w.TargetMap.Equals("Mountain", System.StringComparison.OrdinalIgnoreCase), "the Mountain is not hosted yet");
    }

    [Fact]
    public void CaveArrivalFromTown_IsNotAnExit()
    {
        (MapSpec cave, TileMap map, _) = Load(GameMaps.Mine);

        cave.ResolveWarps(map).Any(w => w.FromX == MineWorld.CaveArrival.X && w.FromY == MineWorld.CaveArrival.Y).Should().BeFalse();
        cave.ResolveWarps(map).Any(w => w.FromX == MineWorld.CaveShaftFront.X && w.FromY == MineWorld.CaveShaftFront.Y).Should().BeFalse("coming up from the first floor must not send the player straight back down");
    }

    [Fact]
    public void Floors_EachHasOneWayUp_ToTheFloorAboveOrTheCave_AndAnElevatorEveryFifth()
    {
        for (int floor = 1; floor <= MineRules.MaxFloor; floor++)
        {
            (MapSpec spec, TileMap map, WalkGrid grid) = Load(MineRules.FloorMapId(floor));
            List<MapWarp> warps = spec.ResolveWarps(map);

            spec.MineFloor.Should().Be(floor);
            spec.IsPrivate.Should().BeTrue("a floor is one player's own");
            warps.Should().ContainSingle();
            warps[0].TargetMap.Should().Be(floor == 1 ? GameMaps.Mine : MineRules.FloorMapId(floor - 1));
            grid.IsBlocked(warps[0].FromX, warps[0].FromY).Should().BeFalse("the way up is the arrival tile");
            (spec.Elevator is not null).Should().Be(MineRules.HasElevator(floor));
            if (spec.Elevator is { } elevator)
            {
                (elevator.TileX, elevator.TileY).Should().Be((warps[0].FromX, warps[0].FromY), "the elevator stands at the arrival tile");
            }
        }
    }

    [Fact]
    public void Floors_TheWayUpArrivesBesideTheWayDown_NotOnIt()
    {
        // Coming up from floor 1 into the cave must not land on the shaft tile itself.
        (MapSpec floor1, TileMap map, _) = Load("mine1");
        MapWarp up = floor1.ResolveWarps(map).Single();

        (up.TargetX, up.TargetY).Should().Be(MineWorld.CaveShaftFront);
        (up.TargetX, up.TargetY).Should().NotBe(MineWorld.CaveShaft);
    }
}
