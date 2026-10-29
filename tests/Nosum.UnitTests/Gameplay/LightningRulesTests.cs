// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The storm's lightning is a pure function of the server clock, so every player sees the same flash and hears the thunder at the same time.</summary>
public sealed class LightningRulesTests
{
    [Fact]
    public void StrikeAtMs_FallsInsideItsOwnWindow()
    {
        for (long slot = 1000; slot < 1020; slot++)
        {
            long strike = LightningRules.StrikeAtMs(slot);

            strike.Should().BeGreaterThanOrEqualTo(slot * LightningRules.SlotMs);
            strike.Should().BeLessThan((slot + 1) * LightningRules.SlotMs);
        }
    }

    [Fact]
    public void StrikeAtMs_VariesFromWindowToWindow()
    {
        int distinct = Enumerable.Range(0, 50)
            .Select(slot => LightningRules.StrikeAtMs(slot) - (slot * LightningRules.SlotMs))
            .Distinct()
            .Count();

        distinct.Should().BeGreaterThan(40);
    }

    [Fact]
    public void SlotOf_ReturnsTheWindowThatHoldsTheTime()
    {
        LightningRules.SlotOf((5 * LightningRules.SlotMs) + 1).Should().Be(5);
        LightningRules.SlotOf((5 * LightningRules.SlotMs) - 1).Should().Be(4);
    }

    [Fact]
    public void FlashAlpha_OutsideTheFlash_IsZero()
    {
        LightningRules.FlashAlpha(-1, reduced: false).Should().Be(0);
        LightningRules.FlashAlpha(LightningRules.FlashMs, reduced: false).Should().Be(0);
        LightningRules.FlashAlpha(LightningRules.FlashMs + 100, reduced: true).Should().Be(0);
    }

    [Fact]
    public void FlashAlpha_FullFlash_IsBrightAndStaysInRange()
    {
        int peak = Enumerable.Range(0, LightningRules.FlashMs).Max(ms => LightningRules.FlashAlpha(ms, reduced: false));

        peak.Should().BeInRange(150, 255);
    }

    [Fact]
    public void FlashAlpha_FullFlash_FlickersTwice()
    {
        LightningRules.FlashAlpha(30, reduced: false).Should().BeGreaterThan(LightningRules.FlashAlpha(70, reduced: false));
        LightningRules.FlashAlpha(190, reduced: false).Should().BeGreaterThan(LightningRules.FlashAlpha(260, reduced: false));
    }

    [Fact]
    public void FlashAlpha_Reduced_IsDimAndSingle()
    {
        int peak = Enumerable.Range(0, LightningRules.FlashMs).Max(ms => LightningRules.FlashAlpha(ms, reduced: true));
        int full = Enumerable.Range(0, LightningRules.FlashMs).Max(ms => LightningRules.FlashAlpha(ms, reduced: false));

        peak.Should().BeInRange(1, 60);
        peak.Should().BeLessThan(full);
        LightningRules.FlashAlpha(190, reduced: true).Should().BeLessThan(LightningRules.FlashAlpha(80, reduced: true));
    }

    [Fact]
    public void ThunderDue_OnlyInItsWindowAfterTheDelay()
    {
        const long slot = 3;
        long due = LightningRules.StrikeAtMs(slot) + LightningRules.ThunderDelayMs;

        LightningRules.ThunderDue(due - 1, slot).Should().BeFalse();
        LightningRules.ThunderDue(due, slot).Should().BeTrue();
        LightningRules.ThunderDue(due + LightningRules.ThunderWindowMs - 1, slot).Should().BeTrue();
        LightningRules.ThunderDue(due + LightningRules.ThunderWindowMs, slot).Should().BeFalse();
    }
}
