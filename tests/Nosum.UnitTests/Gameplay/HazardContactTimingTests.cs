// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Combat;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Regression tests for issue #168: spike contact must be sampled densely enough, and hit at most once per window.</summary>
public sealed class HazardContactTimingTests
{
    [Fact]
    public void IsSpikeTrapActive_IsOpenForAboutEightHundredMsEachCycle()
    {
        // The window is short against the 2.4 s cycle, so sampling only on the 1.5 s AI tick can miss it.
        int openMs = 0;
        for (long t = 0; t < 2400; t++)
        {
            openMs += DungeonHazardRules.IsSpikeTrapActive(0, t) ? 1 : 0;
        }

        openMs.Should().BeInRange(838, 841);
    }

    [Fact]
    public void ContactCooldownMs_SpikeTrapHitsOncePerCycle()
    {
        DungeonHazardRules.ContactCooldownMs(DungeonHazardKind.SpikeTrap).Should().Be(2400);
    }

    [Theory]
    [InlineData(DungeonHazardKind.SporeVent)]
    [InlineData(DungeonHazardKind.MagmaFissure)]
    public void ContactCooldownMs_OtherHazardsKeepTheAiCadence(DungeonHazardKind kind)
    {
        DungeonHazardRules.ContactCooldownMs(kind).Should().Be(1500);
    }
}
