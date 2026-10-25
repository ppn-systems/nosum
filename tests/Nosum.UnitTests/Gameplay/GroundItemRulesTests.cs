// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.GroundItems;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class GroundItemRulesTests
{
    private static readonly Guid s_owner = Guid.NewGuid();
    private static readonly Guid s_cause = Guid.NewGuid();
    private static readonly Guid s_stranger = Guid.NewGuid();

    [Theory]
    [InlineData(100, 100, 100, 100, true)]
    [InlineData(100, 100, 124, 100, true)]
    [InlineData(100, 100, 125, 100, false)]
    [InlineData(100, 100, 100, 76, true)]
    [InlineData(100, 100, 116, 116, true)]
    [InlineData(100, 100, 117, 117, false)]
    public void InPickupRange_UsesAnOneAndAHalfTileRadius(double itemX, double itemY, double feetX, double feetY, bool expected)
        => GroundItemRules.InPickupRange(itemX, itemY, feetX, feetY).Should().Be(expected);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 0)]
    [InlineData(256, 1)]
    [InlineData(2079, 8)]
    [InlineData(-5, 0)]
    public void ChunkOf_GroupsSixteenTilesOfSixteenPixels(double pixel, int expected)
        => GroundItemRules.ChunkOf(pixel, 16).Should().Be(expected);

    [Fact]
    public void Timing_SharedMap_GivesFiveSecondsOfPriorityAndTenMinutesToLive()
    {
        GroundItemRules.Timing(false, 1_000, out long priority, out long expires);

        (priority, expires).Should().Be((6_000, 601_000));
    }

    [Fact]
    public void Timing_PrivateInstance_HasNoPriorityAndNeverExpires()
    {
        GroundItemRules.Timing(true, 1_000, out long priority, out long expires);

        (priority, expires).Should().Be((0, 0));
        GroundItemRules.IsExpired(expires, long.MaxValue).Should().BeFalse();
    }

    [Theory]
    [InlineData(600_000, 599_999, false)]
    [InlineData(600_000, 600_000, true)]
    [InlineData(0, 999_999_999, false)]
    public void IsExpired_IsTrueFromTheExpiryInstant(long expiresAt, long now, bool expected)
        => GroundItemRules.IsExpired(expiresAt, now).Should().Be(expected);

    [Fact]
    public void CanPickUp_SharedMapDuringTheWindow_OnlyTheCause()
    {
        GroundItemRules.CanPickUp(false, Guid.Empty, s_cause, 5_000, s_cause, 4_999).Should().BeTrue();
        GroundItemRules.CanPickUp(false, Guid.Empty, s_cause, 5_000, s_stranger, 4_999).Should().BeFalse();
    }

    [Fact]
    public void CanPickUp_SharedMapAfterTheWindow_Anyone()
        => GroundItemRules.CanPickUp(false, Guid.Empty, s_cause, 5_000, s_stranger, 5_000).Should().BeTrue();

    [Fact]
    public void CanPickUp_SharedMapWithNoCause_Anyone()
        => GroundItemRules.CanPickUp(false, Guid.Empty, null, 5_000, s_stranger, 0).Should().BeTrue();

    [Fact]
    public void CanPickUp_PrivateInstance_OnlyTheOwnerAndTheCauseEvenLongAfter()
    {
        GroundItemRules.CanPickUp(true, s_owner, s_cause, 0, s_owner, 10_000_000).Should().BeTrue();
        GroundItemRules.CanPickUp(true, s_owner, s_cause, 0, s_cause, 10_000_000).Should().BeTrue();
        GroundItemRules.CanPickUp(true, s_owner, s_cause, 0, s_stranger, 10_000_000).Should().BeFalse();
        GroundItemRules.CanPickUp(true, s_owner, null, 0, s_stranger, 10_000_000).Should().BeFalse();
    }

    [Theory]
    [InlineData(8, 8, 8, 24)]
    [InlineData(8, 8, 40, 8)]
    [InlineData(8, 8, -24, 8)]
    [InlineData(8, 8, 30, 30)]
    public void PopPosition_IsAlwaysWithinPickupRangeOfAHarvesterWithinReach(double tileX, double tileY, double feetX, double feetY)
    {
        GroundItemRules.PopPosition(tileX, tileY, feetX, feetY, out double x, out double y);

        GroundItemRules.InPickupRange(x, y, feetX, feetY).Should().BeTrue();
        Math.Sqrt(Math.Pow(x - tileX, 2) + Math.Pow(y - tileY, 2)).Should().BeLessThan(16, "it lies beside the plot, not across the field");
    }

    [Fact]
    public void PopPosition_PlayerNextToThePlot_StaysOnTheTileCentre()
    {
        GroundItemRules.PopPosition(8, 8, 8, 20, out double x, out double y);

        (x, y).Should().Be((8, 8));
    }
}
