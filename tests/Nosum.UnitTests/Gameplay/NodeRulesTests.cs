// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class NodeRulesTests
{
    private static ResourceNode Fresh(NodeKind kind, byte variant = 0, byte stage = TreeStage.Grown)
        => new(1, 4, 5, kind, variant, kind == NodeKind.Tree ? stage : (byte)0, NodeCatalog.HitsFor(kind, stage));

    // Applies the right tool until the node breaks; returns the number of swings and the node left behind.
    private static (int Swings, ResourceNode? Left) SwingUntilBroken(ResourceNode node, ToolKind tool)
    {
        int swings = 0;
        ResourceNode? current = node;
        while (current is { } standing)
        {
            swings++;
            NodeHit hit = NodeRules.Hit(standing, tool);
            hit.Outcome.Should().NotBe(NodeHitOutcome.WrongTool);
            if (hit.Outcome == NodeHitOutcome.Broke)
            {
                return (swings, hit.Next);
            }

            current = hit.Next;
        }

        return (swings, null);
    }

    [Theory]
    [InlineData(NodeKind.Twig, ToolKind.Axe, 1)]
    [InlineData(NodeKind.Stone, ToolKind.Pickaxe, 1)]
    [InlineData(NodeKind.Weed, ToolKind.Scythe, 1)]
    [InlineData(NodeKind.Grass, ToolKind.Scythe, 1)]
    [InlineData(NodeKind.Stump, ToolKind.Axe, 2)]
    [InlineData(NodeKind.CopperOre, ToolKind.Pickaxe, 3)]
    [InlineData(NodeKind.Tree, ToolKind.Axe, 10)]
    public void Hit_WithTheRightTool_BreaksTheNodeInTheDocumentedNumberOfSwings(NodeKind kind, ToolKind tool, int swings)
    {
        (int actual, _) = SwingUntilBroken(Fresh(kind), tool);

        actual.Should().Be(swings);
    }

    [Fact]
    public void Hit_AGrownTree_FallsToAStumpThatKeepsItsIdAndSpecies()
    {
        (_, ResourceNode? left) = SwingUntilBroken(Fresh(NodeKind.Tree, TreeVariant.Pine), ToolKind.Axe);

        left.Should().NotBeNull();
        left!.Value.Should().BeEquivalentTo(new { Id = 1, X = 4, Y = 5, Kind = NodeKind.Stump, Variant = TreeVariant.Pine, Hits = 2 });
        left.Value.Blocks.Should().BeTrue();
    }

    [Theory]
    [InlineData(TreeStage.Seed)]
    [InlineData(TreeStage.Sapling)]
    [InlineData(TreeStage.Bush)]
    public void Hit_AYoungTree_FallsInOneSwingAndLeavesNothing(byte stage)
    {
        (int swings, ResourceNode? left) = SwingUntilBroken(Fresh(NodeKind.Tree, TreeVariant.Oak, stage), ToolKind.Axe);

        swings.Should().Be(1);
        left.Should().BeNull();
    }

    [Theory]
    [InlineData(NodeKind.Tree, ToolKind.Pickaxe)]
    [InlineData(NodeKind.Tree, ToolKind.Scythe)]
    [InlineData(NodeKind.Stone, ToolKind.Axe)]
    [InlineData(NodeKind.CopperOre, ToolKind.Scythe)]
    [InlineData(NodeKind.Weed, ToolKind.Pickaxe)]
    [InlineData(NodeKind.Twig, ToolKind.Hoe)]
    [InlineData(NodeKind.Stone, ToolKind.WateringCan)]
    public void Hit_WithTheWrongTool_ChangesNothing(NodeKind kind, ToolKind tool)
    {
        ResourceNode node = Fresh(kind);

        NodeHit hit = NodeRules.Hit(node, tool);

        hit.Outcome.Should().Be(NodeHitOutcome.WrongTool);
        hit.Next.Should().Be(node);
    }

    [Fact]
    public void Blocks_OnlyTheSolidKindsBlockWalking()
    {
        foreach (NodeKind kind in new[] { NodeKind.Tree, NodeKind.Stump, NodeKind.Twig, NodeKind.Stone, NodeKind.CopperOre })
        {
            Fresh(kind).Blocks.Should().BeTrue(kind.ToString());
        }

        Fresh(NodeKind.Weed).Blocks.Should().BeFalse();
        Fresh(NodeKind.Grass).Blocks.Should().BeFalse();
    }

    [Fact]
    public void NodeCatalog_EveryKindToolIsAnItemInTheCatalogue_AndEveryYieldIsAnItem()
    {
        foreach (NodeDefinition definition in NodeCatalog.All)
        {
            ItemCatalog.All.Should().Contain(i => i.Tool == definition.Tool, definition.Key);
            foreach (NodeYield yield in definition.Yield)
            {
                ItemCatalog.TryGet(yield.ItemId, out _).Should().BeTrue(definition.Key);
                yield.Min.Should().BeInRange(1, yield.Max);
            }
        }

        foreach (byte variant in new[] { TreeVariant.Oak, TreeVariant.Maple, TreeVariant.Pine })
        {
            ItemCatalog.TryGet(NodeCatalog.SeedItemFor(variant), out ItemDefinition? seed).Should().BeTrue();
            seed!.Kind.Should().Be(ItemKind.TreeSeed);
            seed.TreeVariant.Should().Be(variant);
        }
    }

    [Fact]
    public void RollYield_ForTheSameSeed_IsTheSameEveryTime()
    {
        ResourceNode tree = Fresh(NodeKind.Tree, TreeVariant.Maple);

        List<ItemSlot> first = NodeRules.RollYield(tree, 12345);
        List<ItemSlot> second = NodeRules.RollYield(tree, 12345);

        second.Should().Equal(first);
    }

    [Fact]
    public void RollYield_AGrownTree_GivesSixToTenWoodAndSometimesItsOwnSeed()
    {
        ResourceNode tree = Fresh(NodeKind.Tree, TreeVariant.Pine);
        List<List<ItemSlot>> rolls = Enumerable.Range(0, 400).Select(i => NodeRules.RollYield(tree, NodeRandom.Mix(7, 1, i))).ToList();

        rolls.Should().OnlyContain(r => r.Any(s => s.ItemId == ItemCatalog.Wood && s.Quantity >= 6 && s.Quantity <= 10));
        rolls.Select(r => r.Where(s => s.ItemId == ItemCatalog.Wood).Sum(s => s.Quantity)).Distinct().Should().HaveCountGreaterThan(1, "the amount varies");
        int withSeed = rolls.Count(r => r.Any(s => s.ItemId == ItemCatalog.PineSeed));
        withSeed.Should().BeInRange(100, 220, "the seed chance is 40 %");
        rolls.Should().NotContain(r => r.Any(s => s.ItemId == ItemCatalog.OakSeed || s.ItemId == ItemCatalog.MapleSeed), "only the tree's own species");
    }

    [Fact]
    public void RollYield_AStone_GivesStoneAndRarelyCoal()
    {
        ResourceNode stone = Fresh(NodeKind.Stone);
        List<List<ItemSlot>> rolls = Enumerable.Range(0, 2000).Select(i => NodeRules.RollYield(stone, NodeRandom.Mix(9, 1, i))).ToList();

        rolls.Should().OnlyContain(r => r.Any(s => s.ItemId == ItemCatalog.Stone && (s.Quantity == 1 || s.Quantity == 2)));
        rolls.Count(r => r.Any(s => s.ItemId == ItemCatalog.Coal)).Should().BeInRange(40, 170, "coal is a 5 % find");
    }

    [Fact]
    public void RollYield_AWeed_GivesOneFiber_AndGrassOnlySometimes()
    {
        NodeRules.RollYield(Fresh(NodeKind.Weed), 1).Should().Equal(new ItemSlot(ItemCatalog.Fiber, 1));
        NodeRules.RollYield(Fresh(NodeKind.CopperOre), 1).Should().ContainSingle().Which.ItemId.Should().Be(ItemCatalog.CopperOre);

        int grass = Enumerable.Range(0, 500).Count(i => NodeRules.RollYield(Fresh(NodeKind.Grass), NodeRandom.Mix(3, 1, i)).Count > 0);

        grass.Should().BeInRange(130, 270);
    }

    [Fact]
    public void Age_AYoungTreeGrowsStageByStage_AndAGrownTreeStays()
    {
        ResourceNode seed = NodeRules.PlantedTree(1, 2, 3, TreeVariant.Oak);
        seed.Stage.Should().Be(TreeStage.Seed);

        NodeRules.Age(seed, 1).Stage.Should().Be(TreeStage.Seed);
        NodeRules.Age(seed, NodeLimits.DaysPerTreeStage).Stage.Should().Be(TreeStage.Sapling);
        NodeRules.Age(seed, NodeLimits.DaysPerTreeStage * 3).Stage.Should().Be(TreeStage.Grown);
        NodeRules.Age(seed, NodeLimits.DaysPerTreeStage * 3).Hits.Should().Be(10, "a grown tree takes ten swings");
        NodeRules.Age(seed, 365).Stage.Should().Be(TreeStage.Grown);

        ResourceNode grown = Fresh(NodeKind.Tree);
        NodeRules.Age(grown, 30).Should().Be(grown);
        NodeRules.Age(Fresh(NodeKind.Stone), 30).Should().Be(Fresh(NodeKind.Stone));
    }

    // Regression for #153: a starter tool bounces off a node that needs a higher tier, and the node is left exactly as it was.
    [Theory]
    [InlineData(NodeKind.IronOre, ToolKind.Pickaxe)]
    [InlineData(NodeKind.LargeStump, ToolKind.Axe)]
    public void Hit_AStarterToolBelowTheNodesTier_IsWrongToolAndTheNodeStands(NodeKind kind, ToolKind tool)
    {
        ResourceNode node = Fresh(kind);

        NodeRules.Hit(node, tool, 1, ToolTier.Basic).Should().Be(new NodeHit(NodeHitOutcome.WrongTool, node));
        NodeRules.Hit(node, tool).Should().Be(new NodeHit(NodeHitOutcome.WrongTool, node), "the default tool is a starter tool");
    }

    [Theory]
    [InlineData(NodeKind.IronOre, ToolKind.Pickaxe)]
    [InlineData(NodeKind.LargeStump, ToolKind.Axe)]
    public void Hit_AToolOfTheNodesTier_Works(NodeKind kind, ToolKind tool)
    {
        NodeHit hit = NodeRules.Hit(Fresh(kind), tool, 1, ToolTier.Iron);

        hit.Outcome.Should().NotBe(NodeHitOutcome.WrongTool);
    }

    [Fact]
    public void Hit_ACopperOreWithABasicPickaxe_StillWorks()
    {
        NodeHit hit = NodeRules.Hit(Fresh(NodeKind.CopperOre), ToolKind.Pickaxe, 1, ToolTier.Basic);

        hit.Outcome.Should().Be(NodeHitOutcome.Damaged, "only iron ore and the large stump ask for more than a starter tool");
    }

    // Regression for #154: a planted seed, a sapling and a bush are walked over; only the grown tree blocks.
    [Theory]
    [InlineData(TreeStage.Seed)]
    [InlineData(TreeStage.Sapling)]
    [InlineData(TreeStage.Bush)]
    public void Blocks_AYoungTree_IsWalkedOver(byte stage)
    {
        ResourceNode tree = new(1, 4, 5, NodeKind.Tree, TreeVariant.Oak, stage, NodeCatalog.HitsFor(NodeKind.Tree, stage));

        tree.Blocks.Should().BeFalse();
    }

    [Fact]
    public void Blocks_AGrownTreeAndItsStump_Block()
    {
        Fresh(NodeKind.Tree).Blocks.Should().BeTrue();
        Fresh(NodeKind.Stump).Blocks.Should().BeTrue();
    }

    [Fact]
    public void NodeRandom_ForTheSameSeed_IsStableAcrossRuns()
    {
        NodeRandom random = new(42);

        // Pinned values: a change here changes every farm's first fill, so it must be deliberate.
        random.NextUInt64().Should().Be(13679457532755275413UL);
        NodeRandom.HashOf("farm").Should().Be(NodeRandom.HashOf("farm")).And.NotBe(NodeRandom.HashOf("town"));
        NodeRandom.Mix(1, 2, 3).Should().Be(NodeRandom.Mix(1, 2, 3)).And.NotBe(NodeRandom.Mix(1, 3, 2));
    }

    [Fact]
    public void ItemCatalog_ToolsAndTreeSeeds_AreWhatTheNodesNeed()
    {
        ItemCatalog.TryGet(ItemCatalog.Axe, out ItemDefinition? axe).Should().BeTrue();
        axe!.Tool.Should().Be(ToolKind.Axe);
        axe.IsTradable.Should().BeFalse("tools are bound to their owner");
        ItemCatalog.TryGet(ItemCatalog.Pickaxe, out ItemDefinition? pickaxe).Should().BeTrue();
        pickaxe!.Tool.Should().Be(ToolKind.Pickaxe);
        ItemCatalog.TryGet(ItemCatalog.Scythe, out ItemDefinition? scythe).Should().BeTrue();
        scythe!.Tool.Should().Be(ToolKind.Scythe);
        StarterKit.Items.Select(i => i.ItemId).Should().Contain([ItemCatalog.Axe, ItemCatalog.Pickaxe, ItemCatalog.Scythe]);
        StarterKit.Items.Take(5).Select(i => i.ItemId).Should().Equal(ItemCatalog.Hoe, ItemCatalog.WateringCan, ItemCatalog.ParsnipSeeds, ItemCatalog.RadishSeeds, ItemCatalog.CauliflowerSeeds);
        StarterKit.Items.Length.Should().BeLessThanOrEqualTo(InventoryLimits.HotbarSlots);
        foreach (int resource in new[] { ItemCatalog.Wood, ItemCatalog.Stone, ItemCatalog.Fiber, ItemCatalog.Coal, ItemCatalog.CopperOre })
        {
            ItemCatalog.TryGet(resource, out ItemDefinition? item).Should().BeTrue();
            item!.Kind.Should().Be(ItemKind.Resource);
            item.IsTradable.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(NodeKind.CopperOre, ToolKind.Pickaxe, 1, 3, ToolTier.Basic)]
    [InlineData(NodeKind.CopperOre, ToolKind.Pickaxe, 2, 2, ToolTier.Copper)]
    [InlineData(NodeKind.CopperOre, ToolKind.Pickaxe, 3, 1, ToolTier.Iron)]
    [InlineData(NodeKind.IronOre, ToolKind.Pickaxe, 1, 4, ToolTier.Iron)]
    [InlineData(NodeKind.IronOre, ToolKind.Pickaxe, 2, 2, ToolTier.Iron)]
    [InlineData(NodeKind.IronOre, ToolKind.Pickaxe, 3, 2, ToolTier.Iron)]
    [InlineData(NodeKind.Stump, ToolKind.Axe, 2, 1, ToolTier.Basic)]
    public void Hit_AnUpgradedTool_TakesFewerSwings(NodeKind kind, ToolKind tool, int damage, int swingsToBreak, ToolTier tier)
    {
        ResourceNode node = Fresh(kind);
        int swings = 0;
        ResourceNode? current = node;
        while (current is { } standing)
        {
            swings++;
            NodeHit hit = NodeRules.Hit(standing, tool, damage, tier);
            if (hit.Outcome == NodeHitOutcome.Broke)
            {
                break;
            }

            hit.Outcome.Should().Be(NodeHitOutcome.Damaged);
            current = hit.Next;
        }

        swings.Should().Be(swingsToBreak);
    }

    [Fact]
    public void Hit_DamageBelowOne_CountsAsOne()
    {
        NodeHit hit = NodeRules.Hit(Fresh(NodeKind.CopperOre), ToolKind.Pickaxe, 0);

        hit.Next!.Value.Hits.Should().Be(2);
    }

    [Fact]
    public void Hit_AStoneThatHidesTheLadder_LeavesALadderInItsPlace_WhenItBreaks()
    {
        ResourceNode hiding = Fresh(NodeKind.Stone, NodeVariants.LadderStone);

        NodeHit hit = NodeRules.Hit(hiding, ToolKind.Pickaxe);

        hit.Outcome.Should().Be(NodeHitOutcome.Broke);
        hit.Next.Should().NotBeNull();
        hit.Next!.Value.Should().Match<ResourceNode>(n => n.Kind == NodeKind.Ladder && n.X == hiding.X && n.Y == hiding.Y && n.Id == hiding.Id);
        hit.Next!.Value.Blocks.Should().BeTrue("a ladder is faced, not walked over");
        NodeRules.RollYield(hiding, 7).Should().OnlyContain(i => i.ItemId == ItemCatalog.Stone || i.ItemId == ItemCatalog.Coal, "the stone still gives what a stone gives");
    }

    [Fact]
    public void Hit_AnOrdinaryStone_IsGoneWhenItBreaks()
    {
        NodeRules.Hit(Fresh(NodeKind.Stone), ToolKind.Pickaxe).Next.Should().BeNull();
    }

    [Theory]
    [InlineData(ToolKind.Pickaxe)]
    [InlineData(ToolKind.Axe)]
    [InlineData(ToolKind.Hoe)]
    [InlineData(ToolKind.None)]
    public void Hit_ALadder_IsNeverDamaged(ToolKind tool)
    {
        NodeRules.Hit(Fresh(NodeKind.Ladder), tool, 3).Outcome.Should().Be(NodeHitOutcome.WrongTool);
    }

    [Fact]
    public void IronOre_GivesOneToThreeIronOre()
    {
        NodeCatalog.TryGet(NodeKind.IronOre, out NodeDefinition? definition).Should().BeTrue();

        definition!.Yield.Should().ContainSingle().Which.Should().Be(new NodeYield(ItemCatalog.IronOre, 1, 3));
        definition.Blocks.Should().BeTrue();
        for (ulong seed = 0; seed < 50; seed++)
        {
            NodeRules.RollYield(Fresh(NodeKind.IronOre), seed).Single().Quantity.Should().BeInRange(1, 3);
        }
    }
}
