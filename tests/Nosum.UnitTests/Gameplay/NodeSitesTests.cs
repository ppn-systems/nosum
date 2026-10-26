// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class NodeSitesTests
{
    private static TileMap Load(string mapFile) => TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", mapFile)));

    [Theory]
    [InlineData(9, NodeKind.Tree, TreeVariant.Oak)]
    [InlineData(10, NodeKind.Tree, TreeVariant.Maple)]
    [InlineData(11, NodeKind.Tree, TreeVariant.Pine)]
    [InlineData(13, NodeKind.Weed, 0)]
    [InlineData(14, NodeKind.Weed, 0)]
    [InlineData(15, NodeKind.Weed, 0)]
    [InlineData(16, NodeKind.Stone, 0)]
    [InlineData(17, NodeKind.Stone, 0)]
    [InlineData(18, NodeKind.Twig, 0)]
    [InlineData(22, NodeKind.Grass, 0)]
    public void TryMap_TheIndicesTheSheetLabels_BecomeTheirNodeKind(int index, NodeKind kind, byte variant)
    {
        NodeSites.TryMap(index, out NodeKind actual, out byte actualVariant).Should().BeTrue();

        actual.Should().Be(kind);
        actualVariant.Should().Be(variant);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(63)]
    [InlineData(-1)]
    public void TryMap_OtherIndices_PlaceNothing(int index)
    {
        NodeSites.TryMap(index, out NodeKind kind, out _).Should().BeFalse();

        kind.Should().Be(NodeKind.None);
    }

    [Theory]
    [InlineData("Farm.tmx", 65)]
    [InlineData("Forest.tmx", 165)]
    [InlineData("Backwoods.tmx", 165)]
    [InlineData("Town.tmx", 203)]
    [InlineData("BusStop.tmx", 203)]
    public void Read_TheRealMaps_FindsExactlyTheMarkersTheTableKnows(string mapFile, int pathsFirstGid)
    {
        TileMap map = Load(mapFile);
        TileSet paths = map.TileSets.Single(t => t.Name == "Paths");
        paths.FirstGid.Should().Be(pathsFirstGid, "the table is read against the tile set the map really carries");
        TileLayer layer = map.FindLayer("Paths")!;
        int expected = layer.Gids.Count(gid => gid >= paths.FirstGid && gid < paths.FirstGid + paths.TileCount && NodeSites.TryMap(gid - paths.FirstGid, out _, out _));

        List<NodeSite> sites = NodeSites.Read(map);

        sites.Should().HaveCount(expected).And.NotBeEmpty();
        sites.Should().OnlyContain(s => s.X >= 0 && s.X < map.Width && s.Y >= 0 && s.Y < map.Height);
        sites.Select(s => (s.X, s.Y)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Read_TheForest_HasTreesInTheWoodsAndDebrisAmongThem()
    {
        List<NodeSite> sites = NodeSites.Read(Load("Forest.tmx"));

        sites.Count(s => s.Kind == NodeKind.Tree).Should().BeGreaterThan(20);
        sites.Select(s => s.Variant).Where(v => v != 0).Should().Contain([TreeVariant.Maple, TreeVariant.Pine]);
        sites.Should().Contain(s => s.Kind == NodeKind.Weed).And.Contain(s => s.Kind == NodeKind.Grass);
    }

    [Fact]
    public void Read_TheFarm_MarksStonesTwigsAndWeedsToClear()
    {
        List<NodeSite> sites = NodeSites.Read(Load("Farm.tmx"));

        foreach (NodeKind kind in new[] { NodeKind.Tree, NodeKind.Weed, NodeKind.Stone, NodeKind.Twig, NodeKind.Grass })
        {
            sites.Should().Contain(s => s.Kind == kind, kind.ToString());
        }

        sites.Count.Should().BeLessThan(NodeLimits.MaxPerInstance * 2);
    }

    [Fact]
    public void Read_AMapWithoutAPathsLayerOrSheet_HasNoSites()
    {
        NodeSites.Read(Load("FarmHouse.tmx")).Should().BeEmpty();
        NodeSites.Read(new TileMap(1, 1, 16, 16, [], [new TileLayer("Paths", [9])])).Should().BeEmpty("without the Paths tile set the number means nothing");
    }

    [Fact]
    public void Initial_TheRealFarm_FillsTheFarmWithoutBlockingTheFarmhouseDoor()
    {
        TileMap map = Load("Farm.tmx");
        WalkGrid grid = FarmProps.BuildWalkGrid(map);
        List<NodeSite> sites = NodeSites.Read(map);
        (int doorX, int doorY) = FarmProps.FarmhouseDoorFront;
        HashSet<(int X, int Y)> reserved = [(doorX, doorY), (doorX, doorY - 1)];

        List<NodePlacement> placements = NodePlacements.Initial(sites, grid, reserved, NodeRandom.HashOf("farm@x"));

        placements.Should().NotBeEmpty().And.HaveCountLessThan(NodeLimits.MaxPerInstance);
        placements.Should().OnlyContain(p => !grid.IsBlocked(p.X, p.Y), "nothing grows inside a wall");
        placements.Should().NotContain(p => Math.Abs(p.X - doorX) <= 1 && Math.Abs(p.Y - doorY) <= 1 || Math.Abs(p.X - doorX) <= 1 && Math.Abs(p.Y - (doorY - 1)) <= 1, "the doorstep stays free");
        placements.Where(p => p.Kind == NodeKind.Tree).Should().OnlyContain(p => p.Stage == TreeStage.Grown);
        placements.Select(p => (p.X, p.Y)).Should().OnlyHaveUniqueItems();
        placements.Count(p => p.Kind == NodeKind.Tree).Should().Be(sites.Count(s => s.Kind == NodeKind.Tree && !grid.IsBlocked(s.X, s.Y) && !(Math.Abs(s.X - doorX) <= 1 && Math.Abs(s.Y - doorY) <= 2)), "every tree site that is open is planted");
    }
}
