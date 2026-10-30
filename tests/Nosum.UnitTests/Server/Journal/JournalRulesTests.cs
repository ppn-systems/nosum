// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Domain.Journal;
using Xunit;

namespace Nosum.UnitTests.Server.Journal;

/// <summary>The collection journal's pure rules: the first obtaining starts an entry, later ones only count, and only gains are obtained.</summary>
public sealed class JournalRulesTests
{
    private static readonly DateTimeOffset s_first = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid s_player = new("00000000-0000-0000-0000-000000000042");

    [Fact]
    public void Start_FirstObtaining_SetsCountAndFirstTime()
    {
        PlayerDiscovery entry = PlayerDiscovery.Start(s_player, 200, 3, s_first);

        entry.PlayerId.Should().Be(s_player);
        entry.ItemId.Should().Be(200);
        entry.Count.Should().Be(3);
        entry.FirstObtainedAt.Should().Be(s_first);
    }

    [Fact]
    public void Start_ZeroQuantity_IsRefused()
    {
        Action start = () => PlayerDiscovery.Start(s_player, 200, 0, s_first);

        start.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Obtain_LaterObtaining_AddsToTheCountAndKeepsTheFirstTime()
    {
        PlayerDiscovery entry = PlayerDiscovery.Start(s_player, 200, 3, s_first);

        entry.Obtain(2);

        entry.Count.Should().Be(5);
        entry.FirstObtainedAt.Should().Be(s_first);
    }

    [Fact]
    public void Obtain_CountNearTheLimit_SaturatesInsteadOfWrapping()
    {
        PlayerDiscovery entry = PlayerDiscovery.Start(s_player, 200, int.MaxValue - 1, s_first);

        entry.Obtain(10);

        entry.Count.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Obtain_NegativeQuantity_IsRefused()
    {
        PlayerDiscovery entry = PlayerDiscovery.Start(s_player, 200, 3, s_first);

        Action obtain = () => entry.Obtain(-1);

        obtain.Should().Throw<ArgumentOutOfRangeException>();
        entry.Count.Should().Be(3);
    }

    [Fact]
    public void ObtainedTotals_PositiveChanges_AreSummedPerItem()
    {
        (int ItemId, int Delta)[] changes = [(200, 2), (500, 1), (200, 3)];

        JournalRules.ObtainedTotals(changes).Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<int, int> { [200] = 5, [500] = 1 });
    }

    [Fact]
    public void ObtainedTotals_Removals_AreNotObtaining()
    {
        (int ItemId, int Delta)[] changes = [(200, -4), (500, -1)];

        JournalRules.ObtainedTotals(changes).Should().BeEmpty();
    }

    [Fact]
    public void ObtainedTotals_RemovalAndGainOfTheSameItem_CountsOnlyTheGain()
    {
        (int ItemId, int Delta)[] changes = [(200, -5), (200, 2)];

        JournalRules.ObtainedTotals(changes).Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<int, int> { [200] = 2 });
    }

    [Fact]
    public void ObtainedTotals_NoChanges_IsEmpty()
    {
        JournalRules.ObtainedTotals([]).Should().BeEmpty();
    }

    [Fact]
    public void ObtainedTotals_NullChanges_IsRefused()
    {
        Action total = () => JournalRules.ObtainedTotals(null!);

        total.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ObtainedTotals_KeepsTheOrderOfFirstAppearance()
    {
        (int ItemId, int Delta)[] changes = [(500, 1), (200, 1)];

        JournalRules.ObtainedTotals(changes).Keys.ToArray().Should().Equal(500, 200);
    }
}
