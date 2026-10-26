// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Forage nodes: the catalogue, the spots on the maps and the daily spawn (docs/adr/ADR-025 D4).</summary>
public sealed class ForageTests
{
    private static readonly IReadOnlySet<(int X, int Y)> s_none = new HashSet<(int X, int Y)>();

    private static WalkGrid OpenGrid() => new(40, 40, 16, new bool[40 * 40]);

    private static List<NodeSite> Spots(int count)
        => [.. Enumerable.Range(0, count).Select(i => new NodeSite(2 + (i % 30), 2 + (i / 30 * 2), NodeKind.Forage, 0))];

    [Fact]
    public void Catalogue_EveryEntryIsAProduceItemTheBinBuys_AndEverySeasonHasAtLeastThree()
    {
        foreach (ForageEntry entry in ForageCatalog.All)
        {
            ItemCatalog.TryGet(entry.ItemId, out ItemDefinition? item).Should().BeTrue();
            item!.Kind.Should().Be(ItemKind.Produce);
            ShippingPrices.TryGet(entry.ItemId, out _).Should().BeTrue(item.Key);
            item.ObjectIcon.Should().BeGreaterThan(0, item.Key);
            entry.Seasons.Should().NotBe(SeasonSet.None);
        }

        foreach (Season season in System.Enum.GetValues<Season>())
        {
            ForageCatalog.All.Count(e => e.Seasons.Contains(season)).Should().BeGreaterThanOrEqualTo(3, season.ToString());
        }

        ForageCatalog.All.Should().HaveCountLessThan(256, "the variant is a byte");
        ItemCatalog.All.Where(i => i.Id is >= 800 and < 900).Select(i => i.Id).Should().OnlyContain(id => ForageCatalog.All.Any(e => e.ItemId == id), "ids 800-899 are forage");
    }

    [Theory]
    [InlineData(0, Season.Spring)]
    [InlineData(27, Season.Spring)]
    [InlineData(28, Season.Summer)]
    [InlineData(56, Season.Fall)]
    [InlineData(84, Season.Winter)]
    [InlineData(112, Season.Spring)]
    public void SeasonOfDay_FollowsTheCalendar(long nodeDay, Season expected)
    {
        ForageCatalog.SeasonOfDay(nodeDay).Should().Be(expected);
        GameCalendar.Default.SeasonAt(GameCalendar.Default.EpochMs + (nodeDay * GameCalendar.Default.DayLengthMs) + 1).Should().Be(expected, "node day N is the Nth day since the epoch");
    }

    [Fact]
    public void Nodes_Forage_NeverBlocksWalking_AndYieldsItsItem()
    {
        NodeCatalog.TryGet(NodeKind.Forage, out NodeDefinition? definition).Should().BeTrue();
        definition!.Blocks.Should().BeFalse();
        byte variant = (byte)ForageCatalog.All.ToList().FindIndex(e => e.ItemId == ItemCatalog.Leek);
        ResourceNode node = new(1, 3, 3, NodeKind.Forage, variant, 0, NodeCatalog.HitsFor(NodeKind.Forage, 0));

        node.Blocks.Should().BeFalse();
        NodeRules.Hit(node, ToolKind.None).Outcome.Should().Be(NodeHitOutcome.Broke, "an empty hand picks it up in one go");
        NodeRules.Hit(node, ToolKind.Hoe).Outcome.Should().Be(NodeHitOutcome.WrongTool, "the rules ask for no tool; the server passes none for forage");
        NodeRules.RollYield(node, 1).Should().Equal(new ItemSlot(ItemCatalog.Leek, 1));
    }

    [Fact]
    public void Forage_ForTheSameSeed_IsTheSameDay_AndOtherSeedsDiffer()
    {
        List<NodeSite> spots = Spots(60);

        List<NodePlacement> first = NodePlacements.Forage(spots, new NodeField(), OpenGrid(), s_none, s_none, 11, Season.Spring, 0);
        List<NodePlacement> again = NodePlacements.Forage(spots, new NodeField(), OpenGrid(), s_none, s_none, 11, Season.Spring, 0);
        List<NodePlacement> other = NodePlacements.Forage(spots, new NodeField(), OpenGrid(), s_none, s_none, 12, Season.Spring, 0);

        first.Should().NotBeEmpty().And.Equal(again);
        other.Should().NotEqual(first);
    }

    [Theory]
    [InlineData(Season.Spring)]
    [InlineData(Season.Summer)]
    [InlineData(Season.Fall)]
    [InlineData(Season.Winter)]
    public void Forage_PlacesTheSeasonsItemsOnFreeSpots_UpToTheDailyBudget(Season season)
    {
        List<NodeSite> spots = Spots(60);
        NodeField field = new();
        field.Add(new ResourceNode(9, spots[1].X, spots[1].Y, NodeKind.Stone, 0, 0, 1));
        HashSet<(int X, int Y)> occupied = [(spots[2].X, spots[2].Y)];
        HashSet<(int X, int Y)> reserved = [(spots[3].X, spots[3].Y)];

        List<NodePlacement> placements = NodePlacements.Forage(spots, field, OpenGrid(), reserved, occupied, 5, season, 0);

        placements.Should().HaveCount(NodeLimits.ForagePerDay);
        placements.Should().OnlyContain(p => p.Kind == NodeKind.Forage && ForageCatalog.IsInSeason(p.Variant, season));
        placements.Select(p => (p.X, p.Y)).Should().OnlyHaveUniqueItems();
        placements.Should().NotContain(p => p.X == spots[1].X && p.Y == spots[1].Y || p.X == spots[2].X && p.Y == spots[2].Y, "not on a node or under a player");
        placements.Should().NotContain(p => System.Math.Abs(p.X - spots[3].X) <= 1 && System.Math.Abs(p.Y - spots[3].Y) <= 1, "arrival points stay clear");
    }

    [Fact]
    public void Forage_StopsAtTheStandingCap_AndOnMapsWithoutSpots()
    {
        List<NodeSite> spots = Spots(60);

        NodePlacements.Forage(spots, new NodeField(), OpenGrid(), s_none, s_none, 5, Season.Fall, NodeLimits.MaxForageStanding).Should().BeEmpty();
        NodePlacements.Forage(spots, new NodeField(), OpenGrid(), s_none, s_none, 5, Season.Fall, NodeLimits.MaxForageStanding - 2).Should().HaveCount(2);
        NodePlacements.Forage([], new NodeField(), OpenGrid(), s_none, s_none, 5, Season.Fall, 0).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Town.tmx", 203)]
    [InlineData("Forest.tmx", 165)]
    [InlineData("Hillside.tmx", 165)]
    [InlineData("Crossroads.tmx", 203)]
    [InlineData("Farm.tmx", 165)]
    public void ReadForage_TheRealMaps_FindsTheForageGlyphsOfThePathsLayer(string mapFile, int pathsFirstGid)
    {
        TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", mapFile)));
        TileSet paths = map.TileSets.Single(t => t.Name == "Paths");
        paths.FirstGid.Should().Be(pathsFirstGid);
        int expected = map.FindLayer("Paths")!.Gids.Count(gid => gid >= paths.FirstGid && NodeSites.IsForageIndex(gid - paths.FirstGid));

        List<NodeSite> sites = NodeSites.ReadForage(map);

        sites.Should().HaveCount(expected).And.NotBeEmpty();
        sites.Should().OnlyContain(s => s.Kind == NodeKind.Forage);
        sites.Select(s => (s.X, s.Y)).Should().OnlyHaveUniqueItems();
        NodeSites.Read(map).Should().NotContain(s => s.Kind == NodeKind.Forage, "the debris list is unchanged");
    }

    [Fact]
    public void ReadForage_AMapWithoutAPathsLayer_HasNoSpots()
        => NodeSites.ReadForage(TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", "Farmhouse.tmx")))).Should().BeEmpty();
}
