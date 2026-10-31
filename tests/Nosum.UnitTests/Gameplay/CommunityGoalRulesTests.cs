// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The community goal catalogue and its pure rules: delivery validation, reward shares with the cap, the consolation (docs/adr/ADR-030).</summary>
public sealed class CommunityGoalRulesTests
{
    private static CommunityGoalDefinition Goal(long pool = 10_000, long cap = 3_000, int itemId = ItemCatalog.Parsnip, long target = 1_000)
        => new(99, "test", Season.Spring, itemId, target, string.Empty, pool, cap);

    [Fact]
    public void Catalogue_HasUniqueIdsAndKeys_OneGoalPerSeason_OnItemsThatExist()
    {
        CommunityGoalCatalog.All.Select(g => g.Id).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id > 0);
        CommunityGoalCatalog.All.Select(g => g.Key).Should().OnlyHaveUniqueItems();
        foreach (CommunityGoalDefinition goal in CommunityGoalCatalog.All)
        {
            ItemCatalog.TryGet(goal.ItemId, out _).Should().BeTrue($"{goal.Key} asks for an item of the catalogue");
            goal.Target.Should().BePositive();
            goal.CoinCapPerPlayer.Should().BePositive();
            ShippingPrices.TryGet(goal.ItemId, out _).Should().BeTrue($"{goal.Key}: the consolation is half the bin price, so the item must have one");
            CommunityGoalCatalog.ForSeason(goal.Season).Should().Contain(goal);
        }

        foreach (Season season in new[] { Season.Spring, Season.Summer, Season.Fall, Season.Winter })
        {
            CommunityGoalCatalog.ForSeason(season).Should().NotBeEmpty($"{season} has a goal");
        }
    }

    [Fact]
    public void Catalogue_StableIds_AreFrozen()
    {
        // Append-only: the ids are stored in the database and sent on the wire.
        CommunityGoalCatalog.All.Select(g => (g.Id, g.Key)).Should().Equal((1, "spring_bridge"), (2, "summer_festival"), (3, "fall_harvest"), (4, "winter_lanterns"));
        CommunityGoalCatalog.BoardCounterId.Should().Be(1000);
        CommunityGoalCatalog.All.Select(g => g.UnlockKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TryGet_FindsByIdAndRefusesAnUnknownId()
    {
        CommunityGoalCatalog.TryGet(CommunityGoalCatalog.SpringBridge, out CommunityGoalDefinition? goal).Should().BeTrue();
        goal!.Season.Should().Be(Season.Spring);
        CommunityGoalCatalog.TryGet(0, out _).Should().BeFalse();
        CommunityGoalCatalog.TryGet(-3, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(CommunityGoalCatalog.SpringBridge, 1, DeliverVerdict.Ok)]
    [InlineData(CommunityGoalCatalog.SpringBridge, CommunityGoalRules.MaxDeliveryPerRequest, DeliverVerdict.Ok)]
    [InlineData(CommunityGoalCatalog.SpringBridge, 0, DeliverVerdict.InvalidQuantity)]
    [InlineData(CommunityGoalCatalog.SpringBridge, -4, DeliverVerdict.InvalidQuantity)]
    [InlineData(CommunityGoalCatalog.SpringBridge, CommunityGoalRules.MaxDeliveryPerRequest + 1, DeliverVerdict.InvalidQuantity)]
    [InlineData(77, 5, DeliverVerdict.UnknownGoal)]
    public void Validate_ChecksTheGoalAndTheQuantity(int goalId, int quantity, DeliverVerdict expected)
        => CommunityGoalRules.Validate(goalId, quantity).Should().Be(expected);

    [Fact]
    public void Remaining_IsTargetMinusTotal_NeverNegative()
    {
        CommunityGoalRules.Remaining(Goal(target: 1_000), 0).Should().Be(1_000);
        CommunityGoalRules.Remaining(Goal(target: 1_000), 999).Should().Be(1);
        CommunityGoalRules.Remaining(Goal(target: 1_000), 1_000).Should().Be(0);
        CommunityGoalRules.Remaining(Goal(target: 1_000), 1_200).Should().Be(0);
    }

    [Fact]
    public void RewardShare_IsProportionalToWhatWasDelivered_RoundedDown()
    {
        CommunityGoalDefinition goal = Goal(pool: 1_000, cap: 10_000);

        // 1000 delivered in all: 600 / 300 / 100 of the pool.
        CommunityGoalRules.RewardShare(goal, 600, 1_000).Should().Be(600);
        CommunityGoalRules.RewardShare(goal, 300, 1_000).Should().Be(300);
        CommunityGoalRules.RewardShare(goal, 100, 1_000).Should().Be(100);

        // 3 contributors of 333 / 333 / 334 out of 1000: floor shares never add up to more than the pool.
        long sum = CommunityGoalRules.RewardShare(goal, 333, 1_000) + CommunityGoalRules.RewardShare(goal, 333, 1_000) + CommunityGoalRules.RewardShare(goal, 334, 1_000);
        sum.Should().BeLessThanOrEqualTo(1_000).And.BeGreaterThanOrEqualTo(998);
        CommunityGoalRules.RewardShare(goal, 1, 3).Should().Be(333, "1000 / 3 rounded down");
    }

    [Fact]
    public void RewardShare_IsCappedPerPlayer_SoOneDeliveryCannotTakeThePool()
    {
        CommunityGoalDefinition goal = Goal(pool: 50_000, cap: 5_000);

        CommunityGoalRules.RewardShare(goal, 900, 1_000).Should().Be(5_000, "90 % of 50 000 is 45 000, the cap is 5 000");
        CommunityGoalRules.RewardShare(goal, 50, 1_000).Should().Be(2_500, "below the cap the share is exact");
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(10, 0)]
    public void RewardShare_NothingDeliveredOrNoTotal_IsZero(long delivered, long total)
        => CommunityGoalRules.RewardShare(Goal(), delivered, total).Should().Be(0);

    [Fact]
    public void RewardShare_AGoalWithoutAPool_PaysNothing()
        => CommunityGoalRules.RewardShare(Goal(pool: 0), 500, 1_000).Should().Be(0);

    [Fact]
    public void RewardShare_NeverPaysMoreThanTheWholePool_EvenIfDeliveredExceedsTheTotal()
        => CommunityGoalRules.RewardShare(Goal(pool: 1_000, cap: 1_000_000), 2_000, 1_000).Should().Be(1_000);

    [Theory]
    [InlineData(ItemCatalog.Parsnip, 100, 2_000)]
    [InlineData(ItemCatalog.Radish, 100, 3_000)]
    [InlineData(ItemCatalog.Cauliflower, 7, 420)]
    [InlineData(ItemCatalog.Parsnip, 1, 20)]
    [InlineData(ItemCatalog.Parsnip, 0, 0)]
    public void Consolation_IsHalfTheBinPricePerDeliveredItem(int itemId, long delivered, long expected)
        => CommunityGoalRules.Consolation(Goal(itemId: itemId), delivered).Should().Be(expected);

    [Fact]
    public void Consolation_AnItemWithoutABinPrice_IsZero()
        => CommunityGoalRules.Consolation(Goal(itemId: ItemCatalog.Hoe), 50).Should().Be(0);
}
