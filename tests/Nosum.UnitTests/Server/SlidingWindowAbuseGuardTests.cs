// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class SlidingWindowAbuseGuardTests
{
    private static readonly TimeSpan s_hour = TimeSpan.FromHours(1);

    [Fact]
    public void TryAcquire_PastTheLimitInsideTheWindow_Refuses()
    {
        ManualClock clock = new();
        SlidingWindowAbuseGuard guard = new(clock);

        guard.TryAcquire("b", "203.0.113.7", 3, s_hour).Should().BeTrue();
        guard.TryAcquire("b", "203.0.113.7", 3, s_hour).Should().BeTrue();
        guard.TryAcquire("b", "203.0.113.7", 3, s_hour).Should().BeTrue();

        guard.TryAcquire("b", "203.0.113.7", 3, s_hour).Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_AfterTheWindowSlidesPast_AllowsAgain()
    {
        ManualClock clock = new();
        SlidingWindowAbuseGuard guard = new(clock);
        _ = guard.TryAcquire("b", "k", 1, s_hour);
        guard.TryAcquire("b", "k", 1, s_hour).Should().BeFalse();

        clock.Advance(s_hour + TimeSpan.FromSeconds(1));

        guard.TryAcquire("b", "k", 1, s_hour).Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_CountsEachBucketAndKeySeparately()
    {
        SlidingWindowAbuseGuard guard = new(new ManualClock());
        _ = guard.TryAcquire("a", "k", 1, s_hour);

        guard.TryAcquire("a", "other", 1, s_hour).Should().BeTrue();
        guard.TryAcquire("b", "k", 1, s_hour).Should().BeTrue();
        guard.TryAcquire("a", "k", 1, s_hour).Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_LoopbackWhenExempt_IsNeverLimited_OtherAddressesStillAre()
    {
        SlidingWindowAbuseGuard guard = new(new ManualClock(), exemptLoopback: true);

        for (int i = 0; i < 20; i++)
        {
            guard.TryAcquire("b", "127.0.0.1", 1, s_hour).Should().BeTrue();
            guard.TryAcquire("b", "::1", 1, s_hour).Should().BeTrue();
        }

        _ = guard.TryAcquire("b", "198.51.100.2", 1, s_hour);
        guard.TryAcquire("b", "198.51.100.2", 1, s_hour).Should().BeFalse();
    }

    [Fact]
    public void TryAcquire_ManyIdleKeys_AreSweptSoMemoryStaysBounded()
    {
        ManualClock clock = new();
        SlidingWindowAbuseGuard guard = new(clock);
        for (int i = 0; i < SlidingWindowAbuseGuard.MaxTrackedKeys + 10; i++)
        {
            _ = guard.TryAcquire("b", "key" + i, 1, TimeSpan.FromMinutes(1));
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        _ = guard.TryAcquire("b", "fresh", 1, TimeSpan.FromMinutes(1));

        guard.TrackedKeys.Should().BeLessThan(100);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += (long)by.TotalMilliseconds;
    }
}
