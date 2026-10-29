// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Fishing;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The reach of a cast: how far the player may stand from the water tile (docs/adr/ADR-071 D1).</summary>
public sealed class FishingReachTests
{
    private const int TileSize = 16;

    // The feet of a player standing in the centre of tile (5, 5).
    private const double FeetX = 5.5 * TileSize;
    private const double FeetY = 5.5 * TileSize;

    [Fact]
    public void InCastReach_TileNextDoor_IsInReach() => FishingRules.InCastReach(FeetX, FeetY, 6, 5, TileSize).Should().BeTrue();

    [Fact]
    public void InCastReach_ExactlyTheReachAway_IsInReach() => FishingRules.InCastReach(FeetX, FeetY, 5 + FishingRules.CastReachTiles, 5, TileSize).Should().BeTrue();

    [Fact]
    public void InCastReach_OneTileBeyondTheReach_IsOutOfReach() => FishingRules.InCastReach(FeetX, FeetY, 5 + FishingRules.CastReachTiles + 1, 5, TileSize).Should().BeFalse();

    [Fact]
    public void InCastReach_DiagonalBeyondTheRadius_IsOutOfReach() => FishingRules.InCastReach(FeetX, FeetY, 8, 8, TileSize).Should().BeFalse();
}
