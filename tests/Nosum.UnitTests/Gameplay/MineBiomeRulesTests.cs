// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Mines;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The depth tiers of the mine (docs/adr/ADR-070 D1): which biome each floor is in.</summary>
public sealed class MineBiomeRulesTests
{
    [Theory]
    [InlineData(1, MineBiome.Earth)]
    [InlineData(10, MineBiome.Earth)]
    [InlineData(11, MineBiome.Frost)]
    [InlineData(20, MineBiome.Frost)]
    [InlineData(21, MineBiome.Magma)]
    [InlineData(30, MineBiome.Magma)]
    [InlineData(31, MineBiome.Ruins)]
    [InlineData(40, MineBiome.Ruins)]
    public void Of_FloorInATier_IsThatTiersBiome(int floor, MineBiome expected)
    {
        MineBiome biome = MineBiomeRules.Of(floor);

        biome.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public void Of_FloorOutsideTheMine_Throws(int floor)
    {
        Action act = () => MineBiomeRules.Of(floor);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Of_EveryFloor_FallsInExactlyFourEqualTiers()
    {
        int[] counts = new int[4];
        for (int floor = 1; floor <= MineRules.MaxFloor; floor++)
        {
            counts[(int)MineBiomeRules.Of(floor)]++;
        }

        counts.Should().Equal(10, 10, 10, 10);
    }
}
