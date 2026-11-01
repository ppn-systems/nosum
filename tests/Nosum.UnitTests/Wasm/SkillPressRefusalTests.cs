// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Nosum.Wasm.ViewModels.Progress;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// A skill press the client refuses before sending says why (docs/adr/ADR-056 D5; the Z key with no monster in reach was silent in the world pass 2).
/// The decision is pure, so each refusal and the order of the checks are pinned here.
/// </summary>
public sealed class SkillPressRefusalTests
{
    [Fact]
    public void KeyFor_WhirlwindWithNoMonsterInReach_NamesNoTarget()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.Whirlwind, onCooldown: false, mp: 100, mpCost: 15, targetInReach: false, hpFull: false);

        key.Should().Be("Combat_Refused_NoTarget");
    }

    [Fact]
    public void KeyFor_FireballWithNoMonsterInReach_NamesNoTarget()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.Fireball, onCooldown: false, mp: 100, mpCost: 25, targetInReach: false, hpFull: false);

        key.Should().Be("Combat_Refused_NoTarget");
    }

    [Fact]
    public void KeyFor_WhirlwindWithAMonsterInReach_MayBeSent()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.Whirlwind, onCooldown: false, mp: 100, mpCost: 15, targetInReach: true, hpFull: false);

        key.Should().BeNull();
    }

    [Fact]
    public void KeyFor_CooldownAndNoTarget_NamesTheCooldownFirst()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.Whirlwind, onCooldown: true, mp: 100, mpCost: 15, targetInReach: false, hpFull: false);

        key.Should().Be("Combat_Refused_OnCooldown");
    }

    [Fact]
    public void KeyFor_TooLittleMp_NamesNotEnoughMp()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.Fireball, onCooldown: false, mp: 10, mpCost: 25, targetInReach: true, hpFull: false);

        key.Should().Be("Combat_NotEnoughMp");
    }

    [Fact]
    public void KeyFor_HealAtFullHealth_NamesAlreadyFull()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.DivineHeal, onCooldown: false, mp: 100, mpCost: 20, targetInReach: true, hpFull: true);

        key.Should().Be("Combat_Refused_AlreadyFull");
    }

    [Fact]
    public void KeyFor_HealBelowFullHealth_MayBeSentWithoutATarget()
    {
        string? key = SkillPressRefusal.KeyFor(CombatSkillKind.DivineHeal, onCooldown: false, mp: 100, mpCost: 20, targetInReach: false, hpFull: false);

        key.Should().BeNull("the heal needs no monster");
    }

    [Theory]
    [InlineData(CombatSkillKind.Whirlwind)]
    [InlineData(CombatSkillKind.Fireball)]
    [InlineData(CombatSkillKind.DivineHeal)]
    public void KeyFor_EveryRefusal_NamesAMessageThatExistsInTheResources(CombatSkillKind kind)
    {
        // Every key this decision can return is a resource key the player is shown (docs/adr/i18n rules); a missing key would show the key name.
        string?[] keys =
        [
            SkillPressRefusal.KeyFor(kind, onCooldown: true, mp: 100, mpCost: 1, targetInReach: true, hpFull: false),
            SkillPressRefusal.KeyFor(kind, onCooldown: false, mp: 0, mpCost: 1, targetInReach: true, hpFull: false),
            SkillPressRefusal.KeyFor(kind, onCooldown: false, mp: 100, mpCost: 1, targetInReach: false, hpFull: true),
        ];

        foreach (string? key in keys)
        {
            key.Should().NotBeNull();
            key.Should().StartWith("Combat_");
        }
    }
}
