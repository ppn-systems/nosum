// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class NodePlacementsTests
{
    private static readonly IReadOnlySet<(int X, int Y)> s_none = new HashSet<(int X, int Y)>();

    private static WalkGrid OpenGrid() => new(40, 40, 16, new bool[40 * 40]);

    private static List<NodeSite> Sites(NodeKind kind, int count)
        => Enumerable.Range(0, count).Select(i => new NodeSite(2 + (i % 30), 2 + (i / 30 * 2), kind, 0)).ToList();

    [Fact]
    public void Initial_ForTheSameSeed_IsTheSameLayout()
    {
        List<NodeSite> sites = [.. Sites(NodeKind.Weed, 60), .. Sites(NodeKind.Stone, 60).Select(s => s with { Y = s.Y + 8 })];

        List<NodePlacement> first = NodePlacements.Initial(sites, OpenGrid(), s_none, 99);
        List<NodePlacement> again = NodePlacements.Initial(sites, OpenGrid(), s_none, 99);
        List<NodePlacement> other = NodePlacements.Initial(sites, OpenGrid(), s_none, 100);

        again.Should().Equal(first);
        other.Should().NotEqual(first);
    }

    [Fact]
    public void Initial_Trees_AreAlwaysPlantedGrown_AndDebrisIsThinned()
    {
        List<NodeSite> sites = [.. Sites(NodeKind.Tree, 40), .. Sites(NodeKind.Weed, 90).Select(s => s with { Y = s.Y + 10 })];

        List<NodePlacement> placements = NodePlacements.Initial(sites, OpenGrid(), s_none, 5);

        placements.Count(p => p.Kind == NodeKind.Tree).Should().Be(40);
        placements.Where(p => p.Kind == NodeKind.Tree).Should().OnlyContain(p => p.Stage == TreeStage.Grown);
        placements.Count(p => p.Kind == NodeKind.Weed).Should().BeInRange(20, 60, "about two in five weed sites get a weed");
    }

    [Fact]
    public void Initial_SkipsBlockedTilesAndTheNeighbourhoodOfReservedOnes()
    {
        bool[] blocked = new bool[40 * 40];
        blocked[(2 * 40) + 4] = true;
        WalkGrid grid = new(40, 40, 16, blocked);
        List<NodeSite> sites = [new(4, 2, NodeKind.Tree, 0), new(10, 10, NodeKind.Tree, 0), new(11, 11, NodeKind.Tree, 0), new(12, 12, NodeKind.Tree, 0), new(20, 20, NodeKind.Tree, 0)];

        List<NodePlacement> placements = NodePlacements.Initial(sites, grid, new HashSet<(int X, int Y)> { (11, 11) }, 1);

        placements.Select(p => (p.X, p.Y)).Should().Equal((20, 20));
    }

    [Fact]
    public void Initial_NeverExceedsTheInstanceLimit()
    {
        List<NodeSite> sites = [];
        for (int y = 0; y < 40; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                sites.Add(new NodeSite(x, y, NodeKind.Tree, 0));
            }
        }

        NodePlacements.Initial(sites, OpenGrid(), s_none, 1).Should().HaveCount(NodeLimits.MaxPerInstance);
    }

    [Fact]
    public void Regrowth_OnAPublicMap_PlacesAtMostTheDailyBudgetOnFreeTilesOfDebrisSites()
    {
        List<NodeSite> sites = [.. Sites(NodeKind.Weed, 30), .. Sites(NodeKind.Stone, 30).Select(s => s with { Y = s.Y + 8 }), .. Sites(NodeKind.Tree, 10).Select(s => s with { Y = s.Y + 16 })];
        NodeField field = new();
        field.Add(new ResourceNode(1, sites[0].X, sites[0].Y, NodeKind.Weed, 0, 0, 1));

        List<NodePlacement> placements = NodePlacements.Regrowth(sites, field, OpenGrid(), s_none, new HashSet<(int X, int Y)> { (sites[1].X, sites[1].Y) }, 7, isPrivate: false, seasonChanged: false);

        placements.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(NodeLimits.RegrowPerDay);
        placements.Should().OnlyContain(p => p.Kind != NodeKind.Tree && p.Kind != NodeKind.Grass, "only weeds, stones and twigs regrow");
        placements.Select(p => (p.X, p.Y)).Should().OnlyHaveUniqueItems().And.NotContain((sites[0].X, sites[0].Y), "the tile is taken").And.NotContain((sites[1].X, sites[1].Y), "a player stands there");
        NodePlacements.Regrowth(sites, field, OpenGrid(), s_none, s_none, 7, false, false).Should().Equal(NodePlacements.Regrowth(sites, field, OpenGrid(), s_none, s_none, 7, false, false), "deterministic");
    }

    [Fact]
    public void Regrowth_OnAPrivateMap_OnlyGrowsWeeds_AndOnlyWhenASeasonChanges()
    {
        List<NodeSite> sites = [.. Sites(NodeKind.Weed, 30), .. Sites(NodeKind.Stone, 30).Select(s => s with { Y = s.Y + 8 })];
        NodeField field = new();

        NodePlacements.Regrowth(sites, field, OpenGrid(), s_none, s_none, 7, isPrivate: true, seasonChanged: false).Should().BeEmpty();
        List<NodePlacement> atSeasonChange = NodePlacements.Regrowth(sites, field, OpenGrid(), s_none, s_none, 7, isPrivate: true, seasonChanged: true);

        atSeasonChange.Should().NotBeEmpty().And.OnlyContain(p => p.Kind == NodeKind.Weed);
    }

    [Fact]
    public void Regrowth_WithoutSites_DoesNothing()
    {
        NodePlacements.Regrowth([], new NodeField(), OpenGrid(), s_none, s_none, 1, false, false).Should().BeEmpty();
    }
}
