// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Regression tests for issues #147 (counter-attack reach) and #167 (combo scaling of skills).</summary>
public sealed class CombatScalingRegressionTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    public void MonsterStrikesBack_OnlyWithinContactReach(int distanceTiles, bool expected)
    {
        CombatRules.MonsterStrikesBack(distanceTiles).Should().Be(expected);
    }

    [Fact]
    public void ApplyComboMultiplier_RaisesSkillPowerWithTheStreak()
    {
        CombatRules.ApplyComboMultiplier(100, 1).Should().Be(100);
        CombatRules.ApplyComboMultiplier(100, 5).Should().Be(120);
    }

    [Fact]
    public void ApplyComboMultiplier_NonPositiveBase_StaysZero()
    {
        CombatRules.ApplyComboMultiplier(0, 5).Should().Be(0);
    }

    [Fact]
    public void ApplyComboMultiplier_MatchesWeaponSwingScaling()
    {
        CombatRules.ApplyComboMultiplier(CombatRules.CalculateDamage(Nosum.Gameplay.Items.ItemCatalog.IronSword, 0), 7)
            .Should().Be(CombatRules.CalculateDamage(Nosum.Gameplay.Items.ItemCatalog.IronSword, 0, 7));
    }
}
