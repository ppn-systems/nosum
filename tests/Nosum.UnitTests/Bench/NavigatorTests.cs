// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Bench.Load;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Bench;

/// <summary>
/// The load test's bots (docs/adr/ADR-031 D8) against the real maps and the server's own movement and exit rules, without a network: a traveller must reach
/// every public map from its farm, with every position it reports accepted by <see cref="MoveValidator"/>, or the load test would measure refusals instead of players.
/// </summary>
public sealed class NavigatorTests
{
    private static readonly WorldMaps s_world = WorldMaps.Load(RepoFiles.Path("art", "own", "Maps"));

    public static TheoryData<string> PublicMaps
    {
        get
        {
            TheoryData<string> data = [];
            foreach (MapSpec spec in GameMaps.All.Where(m => !m.IsPrivate && !m.IsCoop))
            {
                data.Add(spec.Id);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PublicMaps))]
    public void Traveller_FromTheFarm_ReachesEveryPublicMapWithoutASingleRefusedMove(string target)
    {
        Simulation run = Simulation.Start(traveller: true, target);

        run.RunUntil(static s => s.Navigator.Arrived, limitSeconds: 600);

        run.Navigator.Arrived.Should().BeTrue($"the traveller should reach {target}; it ended on {run.Navigator.Map.Id} at ({run.Navigator.X:0},{run.Navigator.Y:0}) after {run.Seconds:0} s");
        run.Navigator.Map.Id.Should().Be(target);
        run.Refused.Should().Be(0, "every position the bot reports must be one the server accepts");
    }

    [Fact]
    public void Traveller_MeetsATreeTheMapFileDoesNotShow_LearnsFromTheRefusalAndGoesAround()
    {
        MapInfo farm = s_world.Get(GameMaps.Farm);
        MapSpec spec = GameMaps.TryGet(GameMaps.Farm, out MapSpec? found) ? found : throw new InvalidOperationException();
        (double sx, double sy) = SpawnPoint.Find(farm.Grid, spec.SpawnTileX, spec.SpawnTileY);
        MapWarp exit = s_world.Route(GameMaps.Farm, GameMaps.Town)![0];
        WarpRule.TryEdge(exit, farm.Grid, out int ex, out int ey, out _).Should().BeTrue();
        List<(int X, int Y)> path = TilePath.Find(farm.Grid, (WarpRule.TileX(sx, 16), WarpRule.TileY(sy, 16)), (ex, ey))!;
        (int X, int Y) tree = path[path.Count / 2];
        Simulation run = Simulation.Start(traveller: true, GameMaps.Town, tree);

        run.RunUntil(static s => s.Navigator.Arrived, limitSeconds: 900);

        run.Navigator.Arrived.Should().BeTrue("a closed tile is learned from the refusal and the bot takes another way");
        run.Refused.Should().BeInRange(1, 60, "it is refused when it meets the tree, not every step after");
    }

    [Fact]
    public void Route_FarmToTown_GoesThroughTheExitsOfTheMapsInBetween()
    {
        List<MapWarp>? route = s_world.Route(GameMaps.Farm, GameMaps.Town);

        route.Should().NotBeNullOrEmpty();
        route![^1].TargetMap.Should().Be(GameMaps.Town);
        s_world.Route(GameMaps.Town, GameMaps.Town).Should().BeEmpty();
    }

    [Fact]
    public void Route_ToAMapNothingLeadsTo_IsNull()
        => s_world.Route(GameMaps.Farm, "no-such-map").Should().BeNull();

    [Fact]
    public void Farmer_OnTheFarm_WalksToSoilAndUsesTheHoeThenSeedsThenTheCanOnOneTileBeforeTheNext()
    {
        Simulation run = Simulation.Start(traveller: false, GameMaps.Town);

        List<(byte Slot, int X, int Y)> tools = [];
        run.RunUntil(s =>
        {
            if (s.Navigator.TryNextTool(s.Seconds, out (byte Slot, int X, int Y) tool))
            {
                tools.Add(tool);
            }

            return tools.Count >= 6;
        }, limitSeconds: 300);

        tools.Should().HaveCount(6, "a farmer finds soil within five minutes");
        tools.Take(3).Select(t => t.Slot).Should().Equal(0, 2, 1);
        tools.Take(3).Select(t => (t.X, t.Y)).Distinct().Should().HaveCount(1);
        tools.Skip(3).Take(3).Select(t => (t.X, t.Y)).Distinct().Should().HaveCount(1);
        (tools[3].X, tools[3].Y).Should().NotBe((tools[0].X, tools[0].Y), "the second tile is a new one");
        run.Refused.Should().Be(0);
    }

    [Fact]
    public void Farmer_OnAPublicMap_HasNothingToTill()
    {
        Simulation run = Simulation.Start(traveller: true, GameMaps.Town);
        run.RunUntil(static s => s.Navigator.Arrived, limitSeconds: 600);

        run.Navigator.TryNextTool(run.Seconds + 10, out _).Should().BeFalse("only a farm can be tilled");
    }

    [Fact]
    public void TilePath_AroundAWall_FindsTheWayAndRefusesWhenWalledIn()
    {
        bool[] blocked = new bool[5 * 5];
        for (int y = 0; y < 4; y++)
        {
            blocked[(y * 5) + 2] = true;
        }

        WalkGrid open = new(5, 5, 16, blocked);
        WalkGrid walled = new(5, 5, 16, [.. Enumerable.Range(0, 25).Select(i => i % 5 == 2)]);

        List<(int X, int Y)>? around = TilePath.Find(open, (0, 0), (4, 0));
        List<(int X, int Y)>? none = TilePath.Find(walled, (0, 0), (4, 0));

        around.Should().NotBeNull();
        around![^1].Should().Be((4, 0));
        around.Should().Contain((2, 4), "the only gap is the bottom row");
        none.Should().BeNull();
        TilePath.Find(open, (1, 1), (1, 1)).Should().BeEmpty();
    }

    // A tiny server: the same movement check and the same exit rule as PlayerService, 10 reports a second.
    private sealed class Simulation
    {
        private readonly Dictionary<string, WalkGrid> _serverGrids = [];
        private MotionState _motion;

        private Simulation(Navigator navigator, MotionState motion)
        {
            this.Navigator = navigator;
            _motion = motion;
        }

        public Navigator Navigator { get; }

        public int Refused { get; private set; }

        public double Seconds { get; private set; }

        public static Simulation Start(bool traveller, string target, params (int X, int Y)[] hidden)
        {
            MapSpec spec = GameMaps.Farm is { } id && GameMaps.TryGet(id, out MapSpec? farm) ? farm : throw new InvalidOperationException("The farm map is missing.");
            MapInfo map = s_world.Get(spec.Id);
            (double x, double y) = SpawnPoint.Find(map.Grid, spec.SpawnTileX, spec.SpawnTileY);
            Navigator navigator = new(s_world, new Random(42), traveller, target);
            navigator.Enter(map, x, y, 0, 0);
            Simulation run = new(navigator, new MotionState(x, y, 0));

            // Tiles the server has blocked (a tree, a placed object) that the map file does not show.
            if (hidden.Length > 0)
            {
                WalkGrid secret = map.Grid.Clone();
                foreach ((int hx, int hy) in hidden)
                {
                    secret.Block(hx, hy, 1, 1);
                }

                run._serverGrids[map.Id] = secret;
            }

            return run;
        }

        public WalkGrid ServerGrid(MapInfo map) => _serverGrids.GetValueOrDefault(map.Id) ?? map.Grid;

        public void RunUntil(Func<Simulation, bool> done, double limitSeconds)
        {
            while (!done(this) && this.Seconds < limitSeconds)
            {
                this.Seconds += 0.1;
                Position at = this.Navigator.Step(0.1, this.Seconds);
                MoveVerdict verdict = MoveValidator.Validate(ref _motion, at.X, at.Y, at.Facing, at.Moving, this.Seconds, this.ServerGrid(this.Navigator.Map));
                if (verdict != MoveVerdict.Accepted)
                {
                    this.Refused++;
                    this.Navigator.Correct(_motion.X, _motion.Y, verdict == MoveVerdict.Blocked, this.Seconds);
                    continue;
                }

                if (WarpRule.TryFind(this.Navigator.Map.Warps, this.Navigator.Map.Grid, _motion.X, _motion.Y, _motion.Facing, _motion.Moving, out MapWarp warp))
                {
                    MapInfo next = s_world.Get(warp.TargetMap);
                    (double x, double y, int facing) = WarpRule.Arrival(next.Grid, warp.TargetX, warp.TargetY);
                    this.Navigator.Enter(next, x, y, facing, this.Seconds);
                    _motion = new MotionState(x, y, this.Seconds, facing);
                }
            }
        }
    }
}
