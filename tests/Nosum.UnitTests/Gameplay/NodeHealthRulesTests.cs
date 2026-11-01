using FluentAssertions;
using Nosum.Gameplay.Nodes;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Pins the rule of the node hit-points bar (docs/design/critique-gathering.md, finding G1).</summary>
public sealed class NodeHealthRulesTests
{
    [Fact]
    public void Shows_FullNode_IsHidden()
    {
        NodeHealthRules.Shows(10, 10).Should().BeFalse("a node that took no hit carries no mark");
    }

    [Fact]
    public void Shows_OneHitNode_IsHiddenBecauseItBreaksAtOnce()
    {
        NodeHealthRules.Shows(1, 1).Should().BeFalse("a stone, twig or weed has one hit and never stands damaged");
    }

    [Fact]
    public void Shows_DamagedStandingNode_IsShown()
    {
        NodeHealthRules.Shows(9, 10).Should().BeTrue("one hit taken on a tree shows the bar");
        NodeHealthRules.Shows(1, 2).Should().BeTrue("a stump with one hit left shows the bar");
    }

    [Fact]
    public void Shows_NodeWithNoHitsLeft_IsHidden()
    {
        NodeHealthRules.Shows(0, 10).Should().BeFalse("a broken node is not drawn at all");
    }

    [Theory]
    [InlineData(9, 10, 11)]
    [InlineData(5, 10, 6)]
    [InlineData(2, 10, 3)]
    [InlineData(1, 10, 2)]
    [InlineData(1, 3, 4)]
    public void FillPixels_PartOfTheHitsLeft_RoundsUpWithinTheWidth(int hits, int max, int expected)
    {
        NodeHealthRules.FillPixels(hits, max, 12).Should().Be(expected, "the bar is whole pixels, never empty for a standing node");
    }

    [Fact]
    public void FillPixels_ZeroMax_IsTheFullWidth()
    {
        NodeHealthRules.FillPixels(0, 0, 12).Should().Be(12);
    }

    [Theory]
    [InlineData(10, 10, NodeHealthLevel.Healthy)]
    [InlineData(6, 10, NodeHealthLevel.Healthy)]
    [InlineData(5, 10, NodeHealthLevel.Hurt)]
    [InlineData(3, 10, NodeHealthLevel.Hurt)]
    [InlineData(2, 10, NodeHealthLevel.Badly)]
    [InlineData(1, 10, NodeHealthLevel.Badly)]
    public void LevelOf_Thresholds_AreHalfAndAQuarter(int hits, int max, NodeHealthLevel expected)
    {
        NodeHealthRules.LevelOf(hits, max).Should().Be(expected);
    }
}
