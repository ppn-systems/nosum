// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Skill levels from cumulative experience and the experience of each action (docs/adr/ADR-023 D3).</summary>
public sealed class SkillRulesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(379, 1)]
    [InlineData(380, 2)]
    [InlineData(770, 3)]
    [InlineData(1300, 4)]
    [InlineData(2150, 5)]
    [InlineData(3300, 6)]
    [InlineData(4800, 7)]
    [InlineData(6900, 8)]
    [InlineData(9999, 8)]
    [InlineData(10000, 9)]
    [InlineData(14999, 9)]
    [InlineData(15000, 10)]
    [InlineData(1_000_000, 10)]
    public void LevelOf_FollowsTheAdrTable(long xp, int level) => SkillRules.LevelOf(xp).Should().Be(level);

    [Fact]
    public void XpForLevel_IsTheInverseOfLevelOf()
    {
        SkillRules.XpForLevel(0).Should().Be(0);
        for (int level = 1; level <= SkillRules.MaxLevel; level++)
        {
            SkillRules.LevelOf(SkillRules.XpForLevel(level)).Should().Be(level);
            SkillRules.LevelOf(SkillRules.XpForLevel(level) - 1).Should().Be(level - 1);
        }

        SkillRules.XpCap.Should().Be(SkillRules.XpForLevel(SkillRules.MaxLevel));
    }

    [Theory]
    [InlineData(ToolKind.Hoe, SkillKind.Farming)]
    [InlineData(ToolKind.WateringCan, SkillKind.Farming)]
    [InlineData(ToolKind.Axe, SkillKind.Foraging)]
    [InlineData(ToolKind.Scythe, SkillKind.Foraging)]
    [InlineData(ToolKind.Pickaxe, SkillKind.Mining)]
    [InlineData(ToolKind.None, SkillKind.None)]
    public void SkillOf_Tool_IsTheSkillItDiscounts(ToolKind tool, SkillKind skill) => SkillRules.SkillOf(tool).Should().Be(skill);

    [Theory]
    [InlineData(NodeKind.Stone, SkillKind.Mining)]
    [InlineData(NodeKind.CopperOre, SkillKind.Mining)]
    [InlineData(NodeKind.Tree, SkillKind.Foraging)]
    [InlineData(NodeKind.Stump, SkillKind.Foraging)]
    [InlineData(NodeKind.Twig, SkillKind.Foraging)]
    [InlineData(NodeKind.Weed, SkillKind.Foraging)]
    [InlineData(NodeKind.Grass, SkillKind.Foraging)]
    public void SkillOf_Node_IsMiningForStoneAndOre_ForagingForTheRest(NodeKind kind, SkillKind skill) => SkillRules.SkillOf(kind).Should().Be(skill);

    [Fact]
    public void BreakXp_EveryNodeKind_GivesSomething()
    {
        foreach (NodeKind kind in new[] { NodeKind.Tree, NodeKind.Stump, NodeKind.Twig, NodeKind.Stone, NodeKind.CopperOre, NodeKind.Weed, NodeKind.Grass })
        {
            SkillRules.BreakXp(kind).Should().BeGreaterThan(0, kind.ToString());
        }

        SkillRules.BreakXp(NodeKind.Tree).Should().BeGreaterThan(SkillRules.HitXp);
    }

    [Fact]
    public void GatherXp_HitThatLeavesNodeStanding_GivesHitXp()
    {
        SkillRules.GatherXp(NodeKind.Tree, broke: false).Should().Be(SkillRules.HitXp, "a damaging hit gives the hit experience, whatever the node");
        SkillRules.GatherXp(NodeKind.CopperOre, broke: false).Should().Be(SkillRules.HitXp);
    }

    [Fact]
    public void GatherXp_HitThatBreaksNode_GivesBreakXpOfThatKind()
    {
        SkillRules.GatherXp(NodeKind.Tree, broke: true).Should().Be(SkillRules.BreakXp(NodeKind.Tree), "the breaking hit gives the break experience");
        SkillRules.GatherXp(NodeKind.IronOre, broke: true).Should().Be(15);
        SkillRules.GatherXp(NodeKind.Stone, broke: true).Should().Be(4);
    }

    [Fact]
    public void GatherXp_KindWithoutExperience_GivesNothingToFloat()
    {
        SkillRules.GatherXp(NodeKind.Ladder, broke: true).Should().Be(0, "a ladder is not broken and gives no experience");
    }
}
