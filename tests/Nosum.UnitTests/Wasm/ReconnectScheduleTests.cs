// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Wasm.Socket;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class ReconnectScheduleTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Stable = TimeSpan.FromSeconds(10);

    // 0.5 is the centre of the spread: the delay is exactly the backoff, so the expectations read as plain seconds.
    private static readonly Func<double> Centre = () => 0.5;

    [Fact]
    public void NextDelay_BeforeAnyFailure_IsTheBaseDelay()
    {
        ReconnectSchedule schedule = new(Base, Max, Stable, new ManualClock(), Centre);

        schedule.NextDelay().Should().Be(Base);
    }

    [Fact]
    public void Lost_SessionsThatCloseQuickly_DoubleTheDelayUntilTheCap()
    {
        ManualClock clock = new();
        ReconnectSchedule schedule = new(Base, Max, Stable, clock, Centre);
        TimeSpan[] delays = new TimeSpan[6];

        for (int i = 0; i < delays.Length; i++)
        {
            schedule.Connected();
            clock.Advance(TimeSpan.FromSeconds(1));
            schedule.Lost();
            delays[i] = schedule.NextDelay();
        }

        delays.Should().Equal(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(16),
            TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Connected_HandshakeAloneDoesNotResetTheBackoff()
    {
        ManualClock clock = new();
        ReconnectSchedule schedule = new(Base, Max, Stable, clock, Centre);
        for (int i = 0; i < 3; i++)
        {
            schedule.Failed();
        }

        schedule.Connected();
        clock.Advance(TimeSpan.FromSeconds(2));

        schedule.NextDelay().Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Lost_AfterAStableSession_ResetsTheBackoffToTheBase()
    {
        ManualClock clock = new();
        ReconnectSchedule schedule = new(Base, Max, Stable, clock, Centre);
        for (int i = 0; i < 4; i++)
        {
            schedule.Failed();
        }

        schedule.Connected();
        clock.Advance(Stable);
        schedule.Lost();

        schedule.NextDelay().Should().Be(Base);
    }

    [Fact]
    public void Lost_JustBeforeTheStableWindowEnds_KeepsTheBackoff()
    {
        ManualClock clock = new();
        ReconnectSchedule schedule = new(Base, Max, Stable, clock, Centre);
        schedule.Failed();
        schedule.Failed();

        schedule.Connected();
        clock.Advance(Stable - TimeSpan.FromMilliseconds(1));
        schedule.Lost();

        schedule.NextDelay().Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Failed_RaisesTheFailureCountAndTheDelay()
    {
        ReconnectSchedule schedule = new(Base, Max, Stable, new ManualClock(), Centre);

        schedule.Failed();
        schedule.Failed();

        schedule.Failures.Should().Be(2);
        schedule.NextDelay().Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void NextDelay_AtTheLowEndOfTheSpread_IsThreeQuartersOfTheBackoff()
    {
        ReconnectSchedule schedule = new(Base, Max, Stable, new ManualClock(), () => 0.0);

        schedule.NextDelay().Should().Be(TimeSpan.FromSeconds(0.75));
    }

    [Fact]
    public void NextDelay_AtTheHighEndOfTheSpread_StaysBelowFiveQuartersOfTheBackoff()
    {
        ReconnectSchedule schedule = new(Base, Max, Stable, new ManualClock(), () => 0.999999);

        schedule.NextDelay().TotalSeconds.Should().BeInRange(1.2499, 1.25);
    }

    [Fact]
    public void NextDelay_ManyFailures_NeverExceedsQuarterAboveTheCap()
    {
        ReconnectSchedule schedule = new(Base, Max, Stable, new ManualClock(), () => 0.999999);
        for (int i = 0; i < 50; i++)
        {
            schedule.Failed();
        }

        schedule.NextDelay().Should().BeLessOrEqualTo(TimeSpan.FromSeconds(37.5));
    }

    [Fact]
    public void NextDelay_ServersThatFailTogether_DoNotAllWaitTheSameTime()
    {
        // Two clients that lost the server at the same moment draw different spreads, so they retry at different times (issue #151).
        double[] draws = [0.1, 0.9];
        int next = 0;
        ReconnectSchedule first = new(Base, Max, Stable, new ManualClock(), () => draws[next++]);
        ReconnectSchedule second = new(Base, Max, Stable, new ManualClock(), () => draws[next++]);

        first.NextDelay().Should().NotBe(second.NextDelay());
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
