// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Gameplay.Nodes;
using Nosum.Infrastructure.Nodes;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class ToolStateServiceTests
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryBeginSwing_WithinTheCooldown_IsRefused_AndAfterItIsAllowedAgain()
    {
        ManualTime time = new(s_start);
        ToolStateService tools = new(time);
        Guid player = Guid.NewGuid();

        tools.TryBeginSwing(player).Should().BeTrue("the first swing is free");
        tools.TryBeginSwing(player).Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(NodeLimits.SwingCooldownMs - 1));
        tools.TryBeginSwing(player).Should().BeFalse("299 ms is still the same swing");
        time.Advance(TimeSpan.FromMilliseconds(1));
        tools.TryBeginSwing(player).Should().BeTrue("300 ms later the next swing may start");
        tools.TryBeginSwing(player).Should().BeFalse("and starts a new cooldown");
    }

    [Fact]
    public void TryBeginSwing_IsPerPlayer()
    {
        ToolStateService tools = new(new ManualTime(s_start));

        tools.TryBeginSwing(Guid.NewGuid()).Should().BeTrue();
        tools.TryBeginSwing(Guid.NewGuid()).Should().BeTrue("another player has their own cooldown");
    }

    [Fact]
    public async Task Water_StartsFull_RunsOutAfterTheCapacity_AndRefills()
    {
        ToolStateService tools = new(new ManualTime(s_start));
        Guid player = Guid.NewGuid();
        tools.WaterLeft(player).Should().Be(NodeLimits.WateringCanCapacity).And.Be(40);

        for (int i = 1; i <= NodeLimits.WateringCanCapacity; i++)
        {
            (await tools.UseWaterAsync(player)).Should().Be(NodeLimits.WateringCanCapacity - i);
        }

        tools.WaterLeft(player).Should().Be(0);
        (await tools.UseWaterAsync(player)).Should().Be(-1, "an empty can gives nothing");
        (await tools.RefillAsync(player)).Should().Be(NodeLimits.WateringCanCapacity);
        tools.WaterLeft(player).Should().Be(NodeLimits.WateringCanCapacity);
        tools.WaterLeft(Guid.NewGuid()).Should().Be(NodeLimits.WateringCanCapacity, "every can is separate");
    }
}
