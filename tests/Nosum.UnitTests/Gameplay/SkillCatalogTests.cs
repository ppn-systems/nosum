// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Unit tests for combat skill catalog (docs/adr/ADR-056).</summary>
public sealed class SkillCatalogTests
{
    [Theory]
    [InlineData(CombatSkillKind.Whirlwind, 15, 3000, 18, 2, true)]
    [InlineData(CombatSkillKind.Fireball, 25, 4000, 32, 6, false)]
    [InlineData(CombatSkillKind.DivineHeal, 20, 6000, 35, 0, false)]
    public void SkillCatalog_HasCorrectDefinitions(
        CombatSkillKind kind,
        int expectedMp,
        int expectedCd,
        int expectedPower,
        int expectedRange,
        bool expectedAoe)
    {
        SkillCatalog.TryGet(kind, out SkillDefinition? skill).Should().BeTrue();
        skill!.MpCost.Should().Be(expectedMp);
        skill.CooldownMs.Should().Be(expectedCd);
        skill.BasePower.Should().Be(expectedPower);
        skill.Range.Should().Be(expectedRange);
        skill.IsAoe.Should().Be(expectedAoe);
    }

    [Fact]
    public void SkillCatalog_UnknownKind_ReturnsFalse()
    {
        SkillCatalog.TryGet(CombatSkillKind.None, out SkillDefinition? skill).Should().BeFalse();
        skill.Should().BeNull();
    }
}
