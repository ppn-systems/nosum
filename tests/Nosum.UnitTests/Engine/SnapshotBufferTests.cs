// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Core.Interpolation;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class SnapshotBufferTests
{
    [Fact]
    public void TrySample_BetweenTwoSamples_InterpolatesPositionAndKeepsOlderState()
    {
        SnapshotBuffer buffer = new(4);
        buffer.Push(7, 1.0, 0, 0, state: 1);
        buffer.Push(7, 1.1, 10, 20, state: 2);

        bool found = buffer.TrySample(7, 1.05, out EntityPose pose);

        found.Should().BeTrue();
        pose.X.Should().BeApproximately(5, 1e-4f);
        pose.Y.Should().BeApproximately(10, 1e-4f);
        pose.State.Should().Be(1);
        pose.Held.Should().BeFalse();
    }

    [Fact]
    public void TrySample_PastNewestSample_HoldsTheNewestPose()
    {
        SnapshotBuffer buffer = new(4);
        buffer.Push(7, 1.0, 0, 0, 0);
        buffer.Push(7, 1.1, 10, 0, 0);

        buffer.TrySample(7, 2.0, out EntityPose pose);

        pose.X.Should().Be(10);
        pose.Held.Should().BeTrue();
    }

    [Fact]
    public void TrySample_BeforeOldestSample_ReturnsTheOldestPose()
    {
        SnapshotBuffer buffer = new(4);
        buffer.Push(7, 1.0, 3, 4, 0);
        buffer.Push(7, 1.1, 10, 0, 0);

        buffer.TrySample(7, 0.5, out EntityPose pose);

        pose.X.Should().Be(3);
        pose.Held.Should().BeFalse();
    }

    [Fact]
    public void TrySample_UnknownEntity_ReturnsFalse()
    {
        SnapshotBuffer buffer = new(4);

        buffer.TrySample(99, 1.0, out _).Should().BeFalse();
    }

    [Fact]
    public void Push_OlderThanNewest_IsDropped()
    {
        SnapshotBuffer buffer = new(4);
        buffer.Push(7, 1.1, 10, 0, 0);
        buffer.Push(7, 1.0, 99, 0, 0);

        buffer.TrySample(7, 1.1, out EntityPose pose);

        pose.X.Should().Be(10);
    }

    [Fact]
    public void Push_MoreSamplesThanCapacity_ForgetsTheOldest()
    {
        SnapshotBuffer buffer = new(2, samplesPerEntity: 3);
        for (int i = 0; i < 5; i++)
        {
            buffer.Push(1, i, i * 10, 0, 0);
        }

        buffer.TrySample(1, 0, out EntityPose pose);

        pose.X.Should().Be(20);
    }

    [Fact]
    public void Push_MoreEntitiesThanCapacity_IgnoresTheExtraOnes()
    {
        SnapshotBuffer buffer = new(2);
        buffer.Push(1, 1, 0, 0, 0);
        buffer.Push(2, 1, 0, 0, 0);
        buffer.Push(3, 1, 0, 0, 0);

        buffer.Count.Should().Be(2);
        buffer.TrySample(3, 1, out _).Should().BeFalse();
    }

    [Fact]
    public void Remove_MiddleEntity_KeepsTheOthersSamplesIntact()
    {
        SnapshotBuffer buffer = new(4);
        buffer.Push(1, 1, 10, 0, 0);
        buffer.Push(2, 1, 20, 0, 0);
        buffer.Push(3, 1, 30, 0, 0);

        buffer.Remove(1).Should().BeTrue();

        buffer.Count.Should().Be(2);
        buffer.TrySample(3, 1, out EntityPose three);
        buffer.TrySample(2, 1, out EntityPose two);
        three.X.Should().Be(30);
        two.X.Should().Be(20);
        buffer.Remove(1).Should().BeFalse();
    }

    [Fact]
    public void ServerClock_FasterPacketAfterSlowOne_SnapsToTheSmallestDelay()
    {
        ServerClock clock = default;

        clock.Observe(remoteSeconds: 10, localSeconds: 110.30);
        clock.Observe(remoteSeconds: 11, localSeconds: 111.05);

        clock.RemoteNow(111.05).Should().BeApproximately(11, 1e-9);
    }

    [Fact]
    public void ServerClock_SlowerPacket_MovesTheOffsetOnlySlightly()
    {
        ServerClock clock = default;
        clock.Observe(10, 110.05);

        clock.Observe(11, 111.55);

        clock.RemoteNow(111.55).Should().BeApproximately(11.49, 1e-9);
    }
}
