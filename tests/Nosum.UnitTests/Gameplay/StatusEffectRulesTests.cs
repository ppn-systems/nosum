// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Regression tests for issues #132 and #146: speed buffs live in their own slot and carry a speed factor.</summary>
public sealed class StatusEffectRulesTests
{
    [Theory]
    [InlineData(StatusEffectKind.Swiftness, true)]
    [InlineData(StatusEffectKind.BrotherhoodInArms, true)]
    [InlineData(StatusEffectKind.Regeneration, false)]
    [InlineData(StatusEffectKind.Poison, false)]
    [InlineData(StatusEffectKind.None, false)]
    public void IsBuff_SeparatesBuffsFromEffects(StatusEffectKind kind, bool expected)
    {
        StatusEffectRules.IsBuff(kind).Should().Be(expected);
    }

    [Fact]
    public void MoveSpeedFactor_SwiftnessAndBrotherhoodRaiseSpeed()
    {
        StatusEffectRules.MoveSpeedFactor(StatusEffectKind.Swiftness).Should().Be(1.25);
        StatusEffectRules.MoveSpeedFactor(StatusEffectKind.BrotherhoodInArms).Should().Be(1.05);
    }

    [Theory]
    [InlineData(StatusEffectKind.None)]
    [InlineData(StatusEffectKind.Regeneration)]
    [InlineData(StatusEffectKind.Poison)]
    public void MoveSpeedFactor_NonSpeedKinds_AreNormalSpeed(StatusEffectKind kind)
    {
        StatusEffectRules.MoveSpeedFactor(kind).Should().Be(1);
    }

    [Fact]
    public void BrotherhoodInArms_LastsTenMinutes()
    {
        StatusEffectRules.BrotherhoodInArmsSeconds.Should().Be(600);
    }
}
