// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The large stump and the shore forage that the Woods and the Beach add (docs/adr/ADR-037 D2 and D3).</summary>
public sealed class OutdoorRulesTests
{
    [Fact]
    public void LargeStump_IsAnAxeNodeThatBlocksWalking_BreaksInSixSwings_AndGivesALotOfWood()
    {
        _ = NodeCatalog.TryGet(NodeKind.LargeStump, out NodeDefinition? definition).Should().BeTrue();
        definition!.Tool.Should().Be(ToolKind.Axe);
        definition.Blocks.Should().BeTrue();
        definition.Hits.Should().Be(6);
        definition.Yield.Should().ContainSingle().Which.Should().Be(new NodeYield(ItemCatalog.Wood, 8, 12));
        ((byte)NodeKind.LargeStump).Should().Be(11, "the kind is stored as a number: append-only");

        ResourceNode stump = new(1, 5, 5, NodeKind.LargeStump, 0, 0, NodeCatalog.HitsFor(NodeKind.LargeStump, 0));
        definition.RequiredTier.Should().Be(ToolTier.Iron, "a starter axe bounces off it");
        for (int swing = 1; swing < 6; swing++)
        {
            NodeHit hit = NodeRules.Hit(stump, ToolKind.Axe, 1, ToolTier.Iron);
            hit.Outcome.Should().Be(NodeHitOutcome.Damaged, $"swing {swing}");
            stump = hit.Next!.Value;
        }

        NodeRules.Hit(stump, ToolKind.Axe, 1, ToolTier.Iron).Should().Be(new NodeHit(NodeHitOutcome.Broke, null));
        NodeRules.Hit(stump, ToolKind.Pickaxe).Outcome.Should().Be(NodeHitOutcome.WrongTool);
        NodeRules.Hit(stump, ToolKind.Axe, 1, ToolTier.Basic).Outcome.Should().Be(NodeHitOutcome.WrongTool, "a starter axe does nothing to a large stump");
        NodeRules.RollYield(stump, 3).Single().Quantity.Should().BeInRange(8, 12);
        SkillRules.BreakXp(NodeKind.LargeStump).Should().BeGreaterThan(SkillRules.BreakXp(NodeKind.Stump));
    }

    [Fact]
    public void ShoreForage_EverySeasonHasAShell_AndEveryShellIsAProduceItemInTheForageIdRange()
    {
        List<ForageEntry> shore = [.. ForageCatalog.All.Where(e => e.Pool == ForagePools.Shore)];

        shore.Should().HaveCountGreaterThanOrEqualTo(5);
        foreach (Season season in new[] { Season.Spring, Season.Summer, Season.Fall, Season.Winter })
        {
            shore.Count(e => e.Seasons.Contains(season)).Should().BeGreaterThanOrEqualTo(2, season.ToString());
        }

        foreach (ForageEntry entry in shore)
        {
            entry.ItemId.Should().BeInRange(800, 899, "forage ids");
            _ = ItemCatalog.TryGet(entry.ItemId, out ItemDefinition? item).Should().BeTrue();
            item!.Kind.Should().Be(ItemKind.Produce, "the bin buys it");
            item.SellPrice.Should().BeGreaterThan(0);
            item.ObjectIcon.Should().BeGreaterThan(0, "it is drawn from the object sheet");
        }
    }

    [Fact]
    public void Forage_TheWoodlandEntriesKeepTheirVariantNumbers_SoSavedForageStaysTheSame()
    {
        ForageCatalog.All[0].ItemId.Should().Be(ItemCatalog.SpringOnion);
        ForageCatalog.All[13].ItemId.Should().Be(ItemCatalog.SnowYam);
        ForageCatalog.All.Take(14).Should().OnlyContain(e => e.Pool == ForagePools.Woodland);
        ForageCatalog.All.Skip(14).Should().OnlyContain(e => e.Pool == ForagePools.Shore);
    }

    [Fact]
    public void Roll_OnlyReturnsEntriesOfThePoolOfTheSpot()
    {
        NodeRandom random = new(99);
        for (int i = 0; i < 200; i++)
        {
            foreach (Season season in new[] { Season.Spring, Season.Fall })
            {
                ForageCatalog.All[ForageCatalog.Roll(season, ForagePools.Shore, ref random)!.Value].Pool.Should().Be(ForagePools.Shore);
                ForageCatalog.All[ForageCatalog.Roll(season, ForagePools.Woodland, ref random)!.Value].Pool.Should().Be(ForagePools.Woodland);
            }
        }

        ForageCatalog.Roll(Season.Winter, 5, ref random).Should().BeNull("no such pool");
    }

    [Fact]
    public void Scatter_PicksTheSameOpenSitesForTheSameSeed_AndNeverInACorridorOrOutsideItsRectangle()
    {
        // Open ground 10 x 10 with a one-tile corridor on row 0.
        bool[] blocked = new bool[10 * 10];
        for (int x = 0; x < 10; x++)
        {
            blocked[10 + x] = true;
        }

        WalkGrid grid = new(10, 10, 16, blocked);
        NodeScatter scatter = new(NodeKind.Stone, 12, 0, 0, 9, 9);

        List<NodeSite> first = scatter.Sites(grid, 5);

        first.Should().Equal(scatter.Sites(grid, 5));
        first.Should().HaveCount(12).And.OnlyHaveUniqueItems();
        first.Should().OnlyContain(s => s.Kind == NodeKind.Stone && s.Y >= 3 && s.X >= 1 && s.X <= 8, "a site needs open neighbours on four sides");
        new NodeScatter(NodeKind.Twig, 5, 0, 0, 9, 0).Sites(grid, 5).Should().BeEmpty("a corridor has no room");
        new NodeScatter(NodeKind.Twig, 5, 2, 4, 3, 5).Sites(grid, 5).Should().OnlyContain(s => s.X >= 2 && s.X <= 3 && s.Y >= 4 && s.Y <= 5);
    }
}
