// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class SkillCooldownRulesTests
{
    [Fact]
    public void IsReady_BeforeTheReadyInstant_IsFalse()
        => SkillCooldownRules.IsReady(readyAtMs: 10_000, nowMs: 9_999).Should().BeFalse();

    [Fact]
    public void IsReady_AtAndAfterTheReadyInstant_IsTrue()
    {
        SkillCooldownRules.IsReady(readyAtMs: 10_000, nowMs: 10_000).Should().BeTrue("the cooldown has run out at that instant");
        SkillCooldownRules.IsReady(readyAtMs: 10_000, nowMs: 12_000).Should().BeTrue();
    }

    [Fact]
    public void IsReady_NeverCast_IsReady()
        => SkillCooldownRules.IsReady(readyAtMs: 0, nowMs: 1).Should().BeTrue();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadyAfterCast_AddsTheSkillsOwnCooldown(int skillId)
    {
        SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition skill).Should().BeTrue();

        SkillCooldownRules.ReadyAfterCast(5_000, skill).Should().Be(5_000 + skill.CooldownMs);
    }

    [Fact]
    public void ReadyAfterCast_ACastRefusedAfterItSpendsNothing_IsNotReadyForTheFullCooldown()
    {
        // A cast that is admitted at t locks the skill until t + cooldown; a press at t + cooldown - 1 is refused (ADR-056 D5, amended).
        SkillCatalog.TryGet(CombatSkillKind.Fireball, out SkillDefinition fireball).Should().BeTrue();
        long readyAt = SkillCooldownRules.ReadyAfterCast(1_000, fireball);

        SkillCooldownRules.IsReady(readyAt, 1_000 + fireball.CooldownMs - 1).Should().BeFalse();
        SkillCooldownRules.IsReady(readyAt, readyAt).Should().BeTrue();
    }

    [Fact]
    public void RemainingMs_CountsDownAndNeverGoesNegative()
    {
        SkillCooldownRules.RemainingMs(readyAtMs: 4_000, nowMs: 1_000).Should().Be(3_000);
        SkillCooldownRules.RemainingMs(readyAtMs: 4_000, nowMs: 4_000).Should().Be(0);
        SkillCooldownRules.RemainingMs(readyAtMs: 4_000, nowMs: 9_000).Should().Be(0);
    }
}
