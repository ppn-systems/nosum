// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Pacing of farm, node and tool actions (docs/adr/ADR-074): the client's swing never undercuts the server's interval.</summary>
public sealed class ToolPacingTests
{
    [Theory]
    [InlineData(ToolKind.None)]
    [InlineData(ToolKind.Hoe)]
    [InlineData(ToolKind.WateringCan)]
    [InlineData(ToolKind.Axe)]
    [InlineData(ToolKind.Pickaxe)]
    [InlineData(ToolKind.Scythe)]
    [InlineData(ToolKind.Sword)]
    [InlineData(ToolKind.Staff)]
    public void SwingMs_EveryTool_IsNoShorterThanTheServerInterval(ToolKind tool)
    {
        ToolPacing.SwingMs(tool).Should().BeGreaterThanOrEqualTo(ToolPacing.MinIntervalMs);
    }

    [Theory]
    [InlineData(ToolKind.Hoe, true)]
    [InlineData(ToolKind.WateringCan, true)]
    [InlineData(ToolKind.Axe, true)]
    [InlineData(ToolKind.Pickaxe, true)]
    [InlineData(ToolKind.Scythe, true)]
    [InlineData(ToolKind.Sword, false)]
    [InlineData(ToolKind.Staff, false)]
    [InlineData(ToolKind.None, false)]
    public void RepeatsWhileHeld_ToolKinds_OnlyFarmAndMiningToolsRepeat(ToolKind tool, bool expected)
    {
        ToolPacing.RepeatsWhileHeld(tool).Should().Be(expected);
    }

    [Fact]
    public void SwingMs_FarmActions_FitTheRateLimitOfEightPerThreeSeconds()
    {
        // The farm-use handler allows 8 requests in 3 s; a drag at the swing pace (plus recovery) must stay inside it.
        int slowestFarmSwing = ToolPacing.SwingMs(ToolKind.WateringCan) + ToolPacing.RecoveryMs;

        slowestFarmSwing.Should().BeGreaterThanOrEqualTo(3000 / 8);
    }

    [Theory]
    [InlineData(ToolKind.Hoe)]
    [InlineData(ToolKind.Axe)]
    public void SwingMs_Exhausted_IsSlowerThanNormal(ToolKind tool)
    {
        ToolPacing.SwingMs(tool, exhausted: true).Should().BeGreaterThan(ToolPacing.SwingMs(tool, exhausted: false));
    }

    [Fact]
    public void HitFraction_IsInsideTheSwing_AfterTheWindUp()
    {
        ToolPacing.HitFraction.Should().BeInRange(0.3, 0.6);
    }

    [Theory]
    [InlineData(long.MinValue, 1_000, false, "the first use is never too soon")]
    [InlineData(1_000, 1_299, true, "299 ms after the last use is too soon")]
    [InlineData(1_000, 1_300, false, "300 ms after the last use is allowed")]
    [InlineData(1_000, 900, true, "a clock that went back still refuses")]
    public void IsTooSoon_Interval_FollowsTheServerRule(long lastMs, long nowMs, bool expected, string because)
    {
        ToolPacing.IsTooSoon(lastMs, nowMs).Should().Be(expected, because);
    }

    [Theory]
    [InlineData(0, 270, true)]
    [InlineData(67, 270, true)]
    [InlineData(68, 270, false)]
    [InlineData(270, 270, false)]
    [InlineData(0, 0, false)]
    public void IsExhausted_AtOrBelowAQuarter_IsTrue(int energy, int maxEnergy, bool expected)
    {
        ToolPacing.IsExhausted(energy, maxEnergy).Should().Be(expected);
    }
}
