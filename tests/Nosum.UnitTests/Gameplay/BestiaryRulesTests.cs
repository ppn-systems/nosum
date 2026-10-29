// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class BestiaryRulesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 2)]
    [InlineData(99, 2)]
    [InlineData(100, 3)]
    [InlineData(5000, 3)]
    public void TierOf_CountsTheMilestonesReached(int kills, int tier) => BestiaryRules.TierOf(kills).Should().Be(tier);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 50)]
    [InlineData(75, 100)]
    [InlineData(100, 0)]
    public void NextMilestone_IsZeroOnceEveryMilestoneIsReached(int kills, int next) => BestiaryRules.NextMilestone(kills).Should().Be(next);

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void EarnsTitle_OnlyAtTheLastMilestone(int kills, bool earned) => BestiaryRules.EarnsTitle(kills).Should().Be(earned);

    [Fact]
    public void MayEquip_TakingTheTitleOffIsAlwaysAllowed() => BestiaryRules.MayEquip(MonsterKind.None, 0).Should().BeTrue();

    [Fact]
    public void MayEquip_AnUnearnedTitleIsRefused_AndAnEarnedOneIsAllowed()
    {
        BestiaryRules.MayEquip(MonsterKind.CaveBat, 99).Should().BeFalse();
        BestiaryRules.MayEquip(MonsterKind.CaveBat, 100).Should().BeTrue();
    }
}
