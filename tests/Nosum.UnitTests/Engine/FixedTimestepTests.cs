// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Core;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class FixedTimestepTests
{
    private const double Step = 1.0 / 60.0;

    [Fact]
    public void Advance_FrameShorterThanStep_RunsNoStepAndKeepsRemainder()
    {
        FixedTimestep clock = new(Step, 0.25);

        int steps = clock.Advance(Step / 2);

        steps.Should().Be(0);
        clock.Alpha.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Advance_RemaindersAccumulate_RunsStepOnceTheyAddUp()
    {
        FixedTimestep clock = new(Step, 0.25);

        int first = clock.Advance(Step * 0.6);
        int second = clock.Advance(Step * 0.6);

        first.Should().Be(0);
        second.Should().Be(1);
        clock.Alpha.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void Advance_LongPause_IsClampedToMaxFrame()
    {
        FixedTimestep clock = new(0.1, 0.25);

        int steps = clock.Advance(10);

        steps.Should().Be(2);
    }

    [Fact]
    public void Advance_NegativeDelta_CountsAsZero()
    {
        FixedTimestep clock = new(Step, 0.25);

        int steps = clock.Advance(-1);

        steps.Should().Be(0);
        clock.Alpha.Should().Be(0);
    }

    [Fact]
    public void Constructor_MaxFrameShorterThanStep_Throws()
    {
        Action act = () => _ = new FixedTimestep(0.1, 0.05);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
