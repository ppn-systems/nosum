// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Tool tiers (docs/adr/ADR-026 D1): damage, lines, can capacity, and the item ids that carry them.</summary>
public sealed class ToolRulesTests
{
    [Theory]
    [InlineData(ToolTier.Basic, 1, 1, 40)]
    [InlineData(ToolTier.Copper, 2, 3, 55)]
    [InlineData(ToolTier.Iron, 3, 5, 70)]
    public void Tier_DamageLineAndCanCapacity_GrowWithTheTier(ToolTier tier, int damage, int line, int capacity)
    {
        ToolRules.DamagePerHit(tier).Should().Be(damage);
        ToolRules.AreaLength(tier).Should().Be(line);
        ToolRules.WateringCanCapacity(tier).Should().Be(capacity);
    }

    [Fact]
    public void BasicCapacity_IsTheNodeSystemsOriginalCan()
    {
        ToolRules.WateringCanCapacity(ToolTier.Basic).Should().Be(NodeLimits.WateringCanCapacity);
    }

    [Theory]
    [InlineData(ToolKind.Hoe, ItemCatalog.Hoe, ItemCatalog.CopperHoe, ItemCatalog.IronHoe)]
    [InlineData(ToolKind.WateringCan, ItemCatalog.WateringCan, ItemCatalog.CopperWateringCan, ItemCatalog.IronWateringCan)]
    [InlineData(ToolKind.Axe, ItemCatalog.Axe, ItemCatalog.CopperAxe, ItemCatalog.IronAxe)]
    [InlineData(ToolKind.Pickaxe, ItemCatalog.Pickaxe, ItemCatalog.CopperPickaxe, ItemCatalog.IronPickaxe)]
    public void EveryUpgradableTool_HasOneItemPerTier_WithTheSameToolKind(ToolKind tool, int basic, int copper, int iron)
    {
        ToolRules.TryGetItem(tool, ToolTier.Basic, out int b).Should().BeTrue();
        ToolRules.TryGetItem(tool, ToolTier.Copper, out int c).Should().BeTrue();
        ToolRules.TryGetItem(tool, ToolTier.Iron, out int i).Should().BeTrue();

        (b, c, i).Should().Be((basic, copper, iron));
        foreach (int id in new[] { basic, copper, iron })
        {
            ItemCatalog.TryGet(id, out ItemDefinition? item).Should().BeTrue();
            item!.Kind.Should().Be(ItemKind.Tool);
            item.Tool.Should().Be(tool);
            item.IsTradable.Should().BeFalse("tools are bound to their owner");
            id.Should().BeLessThan(100, "tools live in 1-99");
        }

        ToolRules.TierOf(copper).Should().Be(ToolTier.Copper);
        ToolRules.IsUpgradable(tool).Should().BeTrue();
    }

    [Fact]
    public void TheScythe_HasOneTier_AndIsNotUpgradable()
    {
        ToolRules.IsUpgradable(ToolKind.Scythe).Should().BeFalse();
        ToolRules.TryGetItem(ToolKind.Scythe, ToolTier.Copper, out _).Should().BeFalse();
        ToolRules.TierOf(ItemCatalog.Scythe).Should().Be(ToolTier.Basic);
        ToolRules.TierOf(ItemCatalog.Wood).Should().Be(ToolTier.Basic);
    }

    [Theory]
    [InlineData(ItemCatalog.Hoe, 1, AreaVerdict.Ok)]
    [InlineData(ItemCatalog.Hoe, 3, AreaVerdict.TooWide)]
    [InlineData(ItemCatalog.CopperHoe, 3, AreaVerdict.Ok)]
    [InlineData(ItemCatalog.CopperHoe, 5, AreaVerdict.TooWide)]
    [InlineData(ItemCatalog.IronHoe, 5, AreaVerdict.Ok)]
    [InlineData(ItemCatalog.IronHoe, 3, AreaVerdict.Ok)]
    [InlineData(ItemCatalog.CopperWateringCan, 3, AreaVerdict.Ok)]
    [InlineData(ItemCatalog.IronWateringCan, 5, AreaVerdict.Ok)]
    [InlineData(ItemCatalog.IronAxe, 3, AreaVerdict.NotAnAreaTool)]
    [InlineData(ItemCatalog.IronPickaxe, 5, AreaVerdict.NotAnAreaTool)]
    [InlineData(ItemCatalog.Scythe, 3, AreaVerdict.NotAnAreaTool)]
    [InlineData(ItemCatalog.ParsnipSeeds, 3, AreaVerdict.NotAnAreaTool)]
    [InlineData(ItemCatalog.IronHoe, 2, AreaVerdict.InvalidLength)]
    [InlineData(ItemCatalog.IronHoe, 0, AreaVerdict.InvalidLength)]
    [InlineData(ItemCatalog.IronHoe, 7, AreaVerdict.InvalidLength)]
    [InlineData(ItemCatalog.IronHoe, -3, AreaVerdict.InvalidLength)]
    public void ValidateArea_ChecksTheClaimAgainstTheItemInTheSlot(int itemId, int length, AreaVerdict expected)
    {
        ItemCatalog.TryGet(itemId, out ItemDefinition? held).Should().BeTrue();

        ToolRules.ValidateArea(held, length).Should().Be(expected);
    }

    [Fact]
    public void ValidateArea_EmptyHands_OnlyASingleTile()
    {
        ToolRules.ValidateArea(null, 1).Should().Be(AreaVerdict.Ok);
        ToolRules.ValidateArea(null, 3).Should().Be(AreaVerdict.NotAnAreaTool);
    }

    [Theory]
    [InlineData(Facing.Down, 4, 6)]
    [InlineData(Facing.Up, 4, 2)]
    [InlineData(Facing.Left, 2, 4)]
    [InlineData(Facing.Right, 6, 4)]
    public void LineTile_TheThirdTile_IsTwoStepsFromTheFirstTheWayTheFarmerFaces(Facing facing, int expectedX, int expectedY)
    {
        ToolRules.LineTile(4, 4, facing, 3, 0).Should().Be((4, 4), "the first tile is the one in front of the farmer");
        ToolRules.LineTile(4, 4, facing, 3, 2).Should().Be((expectedX, expectedY));
    }

    [Fact]
    public void LineTile_FiveTilesDown_AreConsecutive()
    {
        int[] ys = [.. Enumerable.Range(0, 5).Select(i => ToolRules.LineTile(4, 4, Facing.Down, 5, i).Y)];

        ys.Should().Equal(4, 5, 6, 7, 8);
    }
}
