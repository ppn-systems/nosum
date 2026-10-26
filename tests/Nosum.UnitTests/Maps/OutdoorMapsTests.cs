// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.UnitTests.Gameplay;
using Xunit;

namespace Nosum.UnitTests.Maps;

/// <summary>The Mountain, the Beach and the Secret Woods on the placeholder files (docs/adr/ADR-007, ADR-037): their exits, spawns, the Beach bridge and the nodes placed on them.</summary>
public sealed class OutdoorMapsTests
{
    private static readonly string[] s_newMaps = [GameMaps.Mountain, GameMaps.Beach, GameMaps.Woods];

    private static (MapSpec Spec, TileMap Map, WalkGrid Grid) Load(string id, Func<string, bool>? unlocked = null)
    {
        _ = GameMaps.TryGet(id, out MapSpec? spec).Should().BeTrue(id);
        TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", spec!.TmxFile)));
        return (spec, map, spec.BuildWalkGrid(map, unlocked));
    }

    private static (int X, int Y) TileOf(WalkGrid grid, (double X, double Y, int Facing) arrival)
        => (WarpRule.TileX(arrival.X, grid.TileSize), WarpRule.TileY(arrival.Y, grid.TileSize));

    // An exit whose source tile is a wall (the files list a few of those: Town's (90, -1) and (98, -1), the Mountain's (57, 41) and (85, 41)) can never be used.
    private static bool IsUsable(MapWarp warp, WalkGrid grid) => WarpRule.TryEdge(warp, grid, out int x, out int y, out _) && !grid.IsBlocked(x, y);

    // Where the exits of "from" that lead to "to" put a player, and whether from there a walker can reach an exit of "to" that leads back.
    private static void AssertWayBack(string from, string to)
    {
        (MapSpec fromSpec, TileMap fromMap, WalkGrid fromGrid) = Load(from);
        (MapSpec toSpec, TileMap toMap, WalkGrid toGrid) = Load(to);
        List<MapWarp> forward = [.. fromSpec.ResolveWarps(fromMap).Where(w => w.TargetMap == to && IsUsable(w, fromGrid))];
        List<MapWarp> back = [.. toSpec.ResolveWarps(toMap).Where(w => w.TargetMap == from && IsUsable(w, toGrid))];
        forward.Should().NotBeEmpty($"{from} -> {to}");
        back.Should().NotBeEmpty($"{to} -> {from}");

        foreach (MapWarp warp in forward)
        {
            (double x, double y, int facing) = WarpRule.Arrival(toGrid, warp.TargetX, warp.TargetY);
            MoveValidator.FootBlocked(toGrid, x, y).Should().BeFalse($"{from} -> {to}: {warp}");

            (int startX, int startY) = TileOf(toGrid, (x, y, facing));
            bool[] reachable = MineRulesTests.Reachable(toGrid, startX, startY);
            back.Any(w => WarpRule.TryEdge(w, toGrid, out int ex, out int ey, out _) && reachable[(ey * toGrid.Width) + ex])
                .Should().BeTrue($"a player who arrives at ({startX}, {startY}) of {to} can walk to an exit back to {from}");
        }
    }

    [Theory]
    [InlineData(GameMaps.Town, GameMaps.Mountain)]
    [InlineData(GameMaps.Mountain, GameMaps.Town)]
    [InlineData(GameMaps.Town, GameMaps.Beach)]
    [InlineData(GameMaps.Beach, GameMaps.Town)]
    [InlineData(GameMaps.Forest, GameMaps.Woods)]
    [InlineData(GameMaps.Woods, GameMaps.Forest)]
    [InlineData(GameMaps.Mountain, GameMaps.Backwoods)]
    [InlineData(GameMaps.Backwoods, GameMaps.Mountain)]
    [InlineData(GameMaps.Mountain, GameMaps.Mine)]
    [InlineData(GameMaps.Mine, GameMaps.Mountain)]
    public void Warps_BetweenNeighbours_ResolveBothWays_AndLandOnWalkableTiles(string from, string to) => AssertWayBack(from, to);

    [Fact]
    public void Catalogue_MountainBeachAndWoods_AreSharedOutdoorMaps_WithWeather()
    {
        foreach (string id in s_newMaps)
        {
            _ = GameMaps.TryGet(id, out MapSpec? spec);
            spec!.IsPrivate.Should().BeFalse(id);
            spec.IsCoop.Should().BeFalse(id);
            spec.Farmable.Should().BeFalse(id);
            spec.Placeable.Should().BeFalse(id);
            spec.Indoors.Should().BeFalse($"{id} is under the sky, so weather applies (docs/adr/ADR-027)");
            spec.IsMineFloor.Should().BeFalse(id);
        }
    }

    [Theory]
    [InlineData(GameMaps.Mountain)]
    [InlineData(GameMaps.Beach)]
    [InlineData(GameMaps.Woods)]
    public void Spawn_OfTheNewMaps_IsWalkable_AndConnectedToAnExit(string id)
    {
        (MapSpec spec, TileMap map, WalkGrid grid) = Load(id);
        (double x, double y) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);

        MoveValidator.FootBlocked(grid, x, y).Should().BeFalse();
        bool[] reachable = MineRulesTests.Reachable(grid, WarpRule.TileX(x, grid.TileSize), WarpRule.TileY(y, grid.TileSize));
        spec.ResolveWarps(map).Any(w => WarpRule.TryEdge(w, grid, out int ex, out int ey, out _) && reachable[(ey * grid.Width) + ex]).Should().BeTrue();
    }

    [Fact]
    public void Warps_IntoAndOutOfTheNewMaps_NameTheirTargetTileOnWalkableGround()
    {
        foreach (MapSpec spec in GameMaps.All.Where(m => !m.IsMineFloor))
        {
            (_, TileMap map, WalkGrid grid) = Load(spec.Id);
            foreach (MapWarp warp in spec.ResolveWarps(map).Where(w => (s_newMaps.Contains(w.TargetMap) || s_newMaps.Contains(spec.Id)) && IsUsable(w, grid)))
            {
                (_, _, WalkGrid target) = Load(warp.TargetMap);
                (double x, double y, _) = WarpRule.Arrival(target, warp.TargetX, warp.TargetY);
                MoveValidator.FootBlocked(target, x, y).Should().BeFalse($"{spec.Id}: {warp}");
                Math.Abs((x / target.TileSize) - warp.TargetX).Should().BeLessThan(4, $"{spec.Id}: {warp} arrives close to the tile the file names");
                Math.Abs((y / target.TileSize) - warp.TargetY).Should().BeLessThan(4, $"{spec.Id}: {warp} arrives close to the tile the file names");
            }
        }
    }

    [Fact]
    public void Warps_ExitsToMapsTheWorldDoesNotHost_AreDropped_AndTheFilesOwnMineExitsAreReplaced()
    {
        (MapSpec mountain, TileMap mountainMap, _) = Load(GameMaps.Mountain);
        mountainMap.Properties["Warp"].Should().Contain("Railroad").And.Contain("Tent");
        mountain.ResolveWarps(mountainMap).Select(w => w.TargetMap).Distinct().Should().BeEquivalentTo(GameMaps.Town, GameMaps.Backwoods, GameMaps.Mine);
        mountain.ResolveWarps(mountainMap).Where(w => w.TargetMap == GameMaps.Mine).Should().ContainSingle().Which.Should().Be(new MapWarp(54, 4, GameMaps.Mine, 18, 11));

        (MapSpec woods, TileMap woodsMap, _) = Load(GameMaps.Woods);
        woods.ResolveWarps(woodsMap).Select(w => w.TargetMap).Distinct().Should().BeEquivalentTo(GameMaps.Forest);

        // The Beach's north-east path (file exit at 67, -1) and Town's matching exit (94, 110) are dropped: the east bank is reached over the bridge only.
        (MapSpec beach, TileMap beachMap, _) = Load(GameMaps.Beach);
        beach.ResolveWarps(beachMap).Should().NotContain(w => w.FromX == 67);
        (MapSpec town, TileMap townMap, _) = Load(GameMaps.Town);
        town.ResolveWarps(townMap).Should().NotContain(w => w.FromX == 94 && w.FromY == 110);
        town.ResolveWarps(townMap).Where(w => w.TargetMap == GameMaps.Mine).Should().BeEmpty("the mine is entered from the Mountain");
    }

    [Fact]
    public void BeachBridge_IsBlockedBothWaysUntilTheUnlock_ThenWalkable_AndTheSameGridFollowsTheUnlock()
    {
        bool built = false;
        (_, _, WalkGrid grid) = Load(GameMaps.Beach, key => built && key == CommunityUnlockKeys.BeachBridge);
        (int westX, int westY) = OutdoorWorld.BridgeWestBank;
        (int eastX, int eastY) = OutdoorWorld.BridgeEastBank;
        WalkGrid copy = grid.Clone();

        // Closed: the span is blocked on the grid and its copies, and the east bank cannot be walked to from the west bank.
        for (int x = OutdoorWorld.BridgeFromX; x <= OutdoorWorld.BridgeToX; x++)
        {
            grid.IsBlocked(x, OutdoorWorld.BridgeY).Should().BeTrue($"plank {x}");
            copy.IsBlocked(x, OutdoorWorld.BridgeY).Should().BeTrue($"plank {x} of a copy");
        }

        grid.IsBlocked(westX, westY).Should().BeFalse();
        grid.IsBlocked(eastX, eastY).Should().BeFalse();
        MineRulesTests.Reachable(grid, westX, westY)[(eastY * grid.Width) + eastX].Should().BeFalse("the east bank is behind the broken bridge");
        MoveValidator.FootBlocked(grid, (OutdoorWorld.BridgeFromX * 16) + 8, (OutdoorWorld.BridgeY * 16) + 15).Should().BeTrue();

        // Open: the grid asks the unlock every time, so nothing has to be rebuilt.
        built = true;
        for (int x = OutdoorWorld.BridgeFromX; x <= OutdoorWorld.BridgeToX; x++)
        {
            grid.IsBlocked(x, OutdoorWorld.BridgeY).Should().BeFalse($"plank {x}");
            copy.IsBlocked(x, OutdoorWorld.BridgeY).Should().BeFalse($"plank {x} of a copy");
        }

        MineRulesTests.Reachable(grid, westX, westY)[(eastY * grid.Width) + eastX].Should().BeTrue();
        MoveValidator.FootBlocked(grid, (OutdoorWorld.BridgeFromX * 16) + 8, (OutdoorWorld.BridgeY * 16) + 15).Should().BeFalse();

        // Only the span opens: the water on both sides of it stays blocked.
        grid.IsBlocked(OutdoorWorld.BridgeFromX, OutdoorWorld.BridgeY - 1).Should().BeTrue();
        grid.IsBlocked(OutdoorWorld.BridgeToX, OutdoorWorld.BridgeY + 1).Should().BeTrue();
    }

    [Fact]
    public void BeachBridge_WithNoUnlockSource_StaysShut()
    {
        (_, _, WalkGrid grid) = Load(GameMaps.Beach);

        grid.IsBlocked(OutdoorWorld.BridgeFromX, OutdoorWorld.BridgeY).Should().BeTrue();
        OutdoorWorld.BeachBridge.Key.Should().Be(CommunityUnlockKeys.BeachBridge);
        CommunityGoalCatalog.All.Should().Contain(g => g.UnlockKey == CommunityUnlockKeys.BeachBridge, "a goal is what sets the unlock (docs/adr/ADR-030)");
    }

    [Fact]
    public void BeachBridge_TilesMatchTheFile_TheBrokenBridgeActionSitsOnTheFirstPlank()
    {
        string xml = File.ReadAllText(RepoFiles.Path("art", "own", "Maps", "Beach.tmx"));

        // Tiled objects are in pixels: (928, 208) is tile (58, 13).
        xml.Should().Contain("x=\"" + (OutdoorWorld.BridgeFromX * 16) + "\" y=\"" + (OutdoorWorld.BridgeY * 16) + "\"");
        xml.Should().Contain("BrokenBeachBridge");
    }

    [Fact]
    public void Mountain_TheBridgeOverTheStream_IsOpenedSoTheMineGateCanBeWalkedTo()
    {
        (_, _, WalkGrid grid) = Load(GameMaps.Mountain);
        (double sx, double sy) = SpawnPoint.Find(grid, 15, 38);
        bool[] reachable = MineRulesTests.Reachable(grid, WarpRule.TileX(sx, 16), WarpRule.TileY(sy, 16));

        foreach ((int x, int y) in OutdoorWorld.MountainBridges)
        {
            grid.IsBlocked(x, y).Should().BeFalse();
            reachable[(y * grid.Width) + x].Should().BeTrue();
        }

        reachable[(MineWorld.MountainGate.Y * grid.Width) + MineWorld.MountainGate.X].Should().BeTrue();
    }

    [Theory]
    [InlineData(GameMaps.Mountain)]
    [InlineData(GameMaps.Beach)]
    [InlineData(GameMaps.Woods)]
    public void Nodes_EveryNewMap_HasResourceSitesOnWalkableTiles_AndForageSpots(string id)
    {
        (MapSpec spec, TileMap map, WalkGrid grid) = Load(id, _ => true);
        List<NodeSite> nodes = spec.ResolveNodeSites(map, grid);
        List<NodeSite> forage = spec.ResolveForageSites(map, grid);

        nodes.Should().NotBeEmpty();
        nodes.Select(s => (s.X, s.Y)).Should().OnlyHaveUniqueItems("the store keeps one node per tile");
        forage.Select(s => (s.X, s.Y)).Should().OnlyHaveUniqueItems();
        forage.Should().HaveCountGreaterThan(40);
        forage.Should().OnlyContain(s => s.Kind == NodeKind.Forage);
        nodes.Where(s => spec.ExtraNodeSites.Contains(s) || spec.Scatter.Any(c => c.Kind == s.Kind)).Should().OnlyContain(s => !grid.IsBlocked(s.X, s.Y), "added sites are on open ground");
        forage.Where(s => s.Variant == ForagePools.Shore).All(s => !grid.IsBlocked(s.X, s.Y)).Should().BeTrue();
        forage.Any(s => s.Variant == ForagePools.Shore).Should().Be(id == GameMaps.Beach, "only the Beach has shore spots");

        // The sites a player can walk to from the map's own spawn: most of what was added.
        (double x, double y) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);
        bool[] reachable = MineRulesTests.Reachable(grid, WarpRule.TileX(x, 16), WarpRule.TileY(y, 16));
        nodes.Count(s => reachable[(s.Y * grid.Width) + s.X]).Should().BeGreaterThan(30, "enough sites a player can actually reach");
        foreach (NodeKind kind in new[] { NodeKind.Stone, NodeKind.Twig })
        {
            nodes.Count(s => s.Kind == kind && reachable[(s.Y * grid.Width) + s.X]).Should().BeGreaterThan(0, kind.ToString());
        }
    }

    [Fact]
    public void Nodes_SitesAreTheSameEveryTime_SoTheInitialNodesOfAPublicMapAreStable()
    {
        (MapSpec spec, TileMap map, WalkGrid grid) = Load(GameMaps.Mountain);

        spec.ResolveNodeSites(map, grid).Should().Equal(spec.ResolveNodeSites(map, grid));
        spec.ResolveNodeSites(map, grid).Count(s => s.Kind == NodeKind.Tree).Should().Be(8, "the file marks eight trees; the scatters add no more");
        NodePlacements.Initial(spec.ResolveNodeSites(map, grid), grid, new HashSet<(int X, int Y)>(), 7).Should().Equal(NodePlacements.Initial(spec.ResolveNodeSites(map, grid), grid, new HashSet<(int X, int Y)>(), 7));
    }

    [Fact]
    public void Woods_SixLargeStumps_StandOnOpenReachableGround_AndAllArePlacedAtTheStart()
    {
        (MapSpec spec, TileMap map, WalkGrid grid) = Load(GameMaps.Woods);
        List<NodeSite> sites = spec.ResolveNodeSites(map, grid);
        (double sx, double sy) = SpawnPoint.Find(grid, spec.SpawnTileX, spec.SpawnTileY);
        bool[] reachable = MineRulesTests.Reachable(grid, WarpRule.TileX(sx, 16), WarpRule.TileY(sy, 16));

        sites.Where(s => s.Kind == NodeKind.LargeStump).Should().BeEquivalentTo(OutdoorWorld.WoodsStumps).And.HaveCount(6);
        foreach (NodeSite stump in OutdoorWorld.WoodsStumps)
        {
            grid.IsBlocked(stump.X, stump.Y).Should().BeFalse($"{stump}");
            reachable[(stump.Y * grid.Width) + stump.X].Should().BeTrue($"{stump} can be walked to");
        }

        NodePlacements.Initial(sites, grid, new HashSet<(int X, int Y)>(), 1).Count(p => p.Kind == NodeKind.LargeStump).Should().Be(6);
    }

    [Fact]
    public void Woods_AStumpThatWasChoppedGrowsBack_OnAPublicMapOnly()
    {
        (MapSpec spec, TileMap map, WalkGrid grid) = Load(GameMaps.Woods);
        List<NodeSite> sites = spec.ResolveNodeSites(map, grid);
        HashSet<(int X, int Y)> none = [];

        bool grewBack = Enumerable.Range(1, 200).Any(seed => NodePlacements.Regrowth(sites, new NodeField(), grid, none, none, (ulong)seed, isPrivate: false, seasonChanged: false).Any(p => p.Kind == NodeKind.LargeStump));
        bool grewOnPrivate = Enumerable.Range(1, 200).Any(seed => NodePlacements.Regrowth(sites, new NodeField(), grid, none, none, (ulong)seed, isPrivate: true, seasonChanged: true).Any(p => p.Kind == NodeKind.LargeStump));

        grewBack.Should().BeTrue();
        grewOnPrivate.Should().BeFalse("a private map only regrows weeds (docs/adr/ADR-021)");
    }

    [Theory]
    [InlineData(Season.Spring)]
    [InlineData(Season.Summer)]
    [InlineData(Season.Fall)]
    [InlineData(Season.Winter)]
    public void Forage_OnTheBeachIsShoreItems_OnTheMountainAndWoodsWoodlandOnes_InSeason(Season season)
    {
        (MapSpec beach, TileMap beachMap, WalkGrid beachGrid) = Load(GameMaps.Beach);
        List<NodePlacement> shells = NodePlacements.Forage(beach.ResolveForageSites(beachMap, beachGrid), new NodeField(), beachGrid, new HashSet<(int X, int Y)>(), new HashSet<(int X, int Y)>(), 3, season, 0);

        shells.Should().HaveCount(NodeLimits.ForagePerDay);
        foreach (NodePlacement placement in shells)
        {
            _ = ForageCatalog.TryGet(placement.Variant, out ForageEntry entry).Should().BeTrue();
            entry.Pool.Should().Be(ForagePools.Shore);
            entry.Seasons.Contains(season).Should().BeTrue();
            ItemCatalogHas(entry.ItemId).Should().BeTrue();
        }

        foreach (string id in new[] { GameMaps.Mountain, GameMaps.Woods })
        {
            (MapSpec spec, TileMap map, WalkGrid grid) = Load(id);
            List<NodePlacement> plants = NodePlacements.Forage(spec.ResolveForageSites(map, grid), new NodeField(), grid, new HashSet<(int X, int Y)>(), new HashSet<(int X, int Y)>(), 3, season, 0);
            plants.Should().NotBeEmpty(id);
            plants.Should().OnlyContain(p => ForageCatalog.All[p.Variant].Pool == ForagePools.Woodland, id);
        }
    }

    private static bool ItemCatalogHas(int itemId) => Nosum.Gameplay.Items.ItemCatalog.TryGet(itemId, out _);
}
