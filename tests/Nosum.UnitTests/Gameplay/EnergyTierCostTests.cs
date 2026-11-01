using FluentAssertions;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Pins the tier discount of an upgraded axe or pickaxe (docs/adr/ADR-089 D1).</summary>
public sealed class EnergyTierCostTests
{
    [Theory]
    [InlineData(ToolTier.Basic, 0)]
    [InlineData(ToolTier.Copper, 1)]
    [InlineData(ToolTier.Iron, 2)]
    public void TierDiscount_Axe_GrowsWithTheTier(ToolTier tier, int expected)
    {
        EnergyRules.TierDiscount(ToolKind.Axe, tier).Should().Be(expected);
        EnergyRules.TierDiscount(ToolKind.Pickaxe, tier).Should().Be(expected);
    }

    [Theory]
    [InlineData(ToolKind.Hoe)]
    [InlineData(ToolKind.WateringCan)]
    [InlineData(ToolKind.Scythe)]
    public void TierDiscount_OtherTools_IsAlwaysZero(ToolKind tool)
    {
        EnergyRules.TierDiscount(tool, ToolTier.Iron).Should().Be(0, "the hoe and the can buy a longer line, not less energy");
    }

    [Fact]
    public void Cost_BasicAxeAtLevelZero_IsThree()
    {
        EnergyRules.Cost(ToolKind.Axe, 0, ToolTier.Basic).Should().Be(3);
    }

    [Fact]
    public void Cost_CopperAxeAtLevelZero_IsTwo()
    {
        EnergyRules.Cost(ToolKind.Axe, 0, ToolTier.Copper).Should().Be(2, "a copper axe saves one energy per swing");
    }

    [Fact]
    public void Cost_IronPickaxeAtLevelZero_IsOne()
    {
        EnergyRules.Cost(ToolKind.Pickaxe, 0, ToolTier.Iron).Should().Be(1);
    }

    [Fact]
    public void Cost_CopperAxeAtLevelFour_IsOne_AndNeverBelowOne()
    {
        EnergyRules.Cost(ToolKind.Axe, 4, ToolTier.Copper).Should().Be(1, "the skill discount and the tier discount add up, down to the minimum of one");
        EnergyRules.Cost(ToolKind.Axe, 10, ToolTier.Iron).Should().Be(1);
    }

    [Fact]
    public void Cost_CopperHoe_IsStillThree()
    {
        EnergyRules.Cost(ToolKind.Hoe, 0, ToolTier.Copper).Should().Be(3);
    }

    [Fact]
    public void Cost_Scythe_StaysFreeAtAnyTier()
    {
        EnergyRules.Cost(ToolKind.Scythe, 0, ToolTier.Iron).Should().Be(0);
    }

    [Fact]
    public void Cost_TwoArgumentOverload_MatchesTheBasicTier()
    {
        EnergyRules.Cost(ToolKind.Axe, 4).Should().Be(EnergyRules.Cost(ToolKind.Axe, 4, ToolTier.Basic));
    }

    [Fact]
    public void Cost_CopperAxeBreaksATreeInFiveSwings_TenEnergyNotThirty()
    {
        // Tree: 10 hits; damage per swing = tier + 1 (ToolRules.DamagePerHit). Energy per swing from the cost above.
        int swings = (int)System.Math.Ceiling(10 / (double)ToolRules.DamagePerHit(ToolTier.Copper));
        int energy = swings * EnergyRules.Cost(ToolKind.Axe, 0, ToolTier.Copper);
        swings.Should().Be(5);
        energy.Should().Be(10);
        int basicSwings = (int)System.Math.Ceiling(10 / (double)ToolRules.DamagePerHit(ToolTier.Basic));
        (basicSwings * EnergyRules.Cost(ToolKind.Axe, 0, ToolTier.Basic)).Should().Be(30, "a basic axe spends thirty energy on the same tree");
    }
}
