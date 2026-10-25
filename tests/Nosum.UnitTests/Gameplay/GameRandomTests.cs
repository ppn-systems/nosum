// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.GroundItems;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class GameRandomTests
{
    [Fact]
    public void NextUInt64_SameSeed_GivesTheSameSequence()
    {
        GameRandom a = new(12345);
        GameRandom b = new(12345);

        Enumerable.Range(0, 20).Select(_ => a.NextUInt64()).Should().Equal(Enumerable.Range(0, 20).Select(_ => b.NextUInt64()));
    }

    [Fact]
    public void NextUInt64_DifferentSeeds_GiveDifferentSequences()
        => new GameRandom(1).NextUInt64().Should().NotBe(new GameRandom(2).NextUInt64());

    [Fact]
    public void NextUInt64_KnownSeed_MatchesTheSplitMix64Reference()
    {
        // First outputs of SplitMix64 seeded with zero (the published reference values).
        GameRandom random = new(0);

        random.NextUInt64().Should().Be(0xE220A8397B1DCDAFUL);
        random.NextUInt64().Should().Be(0x6E789E6AA1B965F4UL);
    }

    [Fact]
    public void Range_StaysInsideTheBoundsAndReachesBothEnds()
    {
        GameRandom random = new(7);
        int[] rolls = [.. Enumerable.Range(0, 2000).Select(_ => random.Range(6, 10))];

        rolls.Should().OnlyContain(r => r >= 6 && r <= 10);
        rolls.Distinct().Should().BeEquivalentTo([6, 7, 8, 9, 10]);
    }

    [Fact]
    public void Range_SingleValue_ReturnsIt() => new GameRandom(3).Range(4, 4).Should().Be(4);

    [Fact]
    public void Range_MaxBelowMin_Throws()
    {
        GameRandom random = new(3);
        Action act = () => random.Range(5, 4);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1000)]
    public void Chance_ZeroNeverAndHundredAlways(int percent, int expectedHits)
    {
        GameRandom random = new(99);

        Enumerable.Range(0, 1000).Count(_ => random.Chance(percent)).Should().Be(expectedHits);
    }

    [Fact]
    public void Chance_TwentyPercent_HitsAboutAFifth()
    {
        GameRandom random = new(2026);

        int hits = Enumerable.Range(0, 10_000).Count(_ => random.Chance(20));

        hits.Should().BeInRange(1_800, 2_200);
    }

    [Fact]
    public void Mix_IsDeterministicAndOrderSensitive()
    {
        GameRandom.Mix(1, 2).Should().Be(GameRandom.Mix(1, 2));
        GameRandom.Mix(1, 2).Should().NotBe(GameRandom.Mix(2, 1));
    }

    [Fact]
    public void ItemYield_RollWithASeed_IsReproducibleAndHonoursTheRange()
    {
        ItemYield wood = new(1, 6, 10);
        ItemYield seed = new(2, 1, 1, ChancePercent: 30);

        int[] first = [.. Enumerable.Range(0, 50).Select(i => wood.Roll(new GameRandom(GameRandom.Mix(77, (ulong)i))))];
        int[] second = [.. Enumerable.Range(0, 50).Select(i => wood.Roll(new GameRandom(GameRandom.Mix(77, (ulong)i))))];

        first.Should().Equal(second);
        first.Should().OnlyContain(q => q >= 6 && q <= 10);
        GameRandom dice = new(5);
        int hits = Enumerable.Range(0, 5_000).Count(_ => seed.Roll(dice) == 1);
        hits.Should().BeInRange(1_300, 1_700);
        ItemYield.Fixed(3, 2).Roll(new GameRandom(1)).Should().Be(2);
    }
}
