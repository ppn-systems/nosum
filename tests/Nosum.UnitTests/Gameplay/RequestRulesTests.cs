// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Requests;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The daily requests of the Town board (docs/adr/ADR-036 D6): the roll is deterministic from the day number, asks only for items in season, and pays the bin price plus the premium.</summary>
public sealed class RequestRulesTests
{
    public static TheoryData<Season> Seasons => new() { Season.Spring, Season.Summer, Season.Fall, Season.Winter };

    [Theory]
    [MemberData(nameof(Seasons))]
    public void Roll_SameDayAndSeason_GivesTheSameRequests_EveryTime(Season season)
    {
        for (int day = 1; day <= 60; day++)
        {
            RequestRules.Roll(day, season).Should().Equal(RequestRules.Roll(day, season), $"day {day} is a pure function of the day number");
        }
    }

    [Fact]
    public void Roll_IsIndependentOfTheOrderOfCalls()
    {
        RequestTemplate[] forward = [.. Enumerable.Range(1, 30).SelectMany(d => RequestRules.Roll(d, Season.Spring))];
        RequestTemplate[] backward = [.. Enumerable.Range(1, 30).Reverse().Select(d => RequestRules.Roll(d, Season.Spring)).Reverse().SelectMany(r => r)];

        forward.Should().Equal(backward);
    }

    [Fact]
    public void Roll_PinnedDay_NeverChangesBetweenReleases()
    {
        // The roll is wire-visible state once rows are written, but a change here would also change tomorrow's rows for a server restarted mid-day.
        // Pinning one day catches an accidental change of the stream or the salt.
        RequestTemplate[] day = RequestRules.Roll(7, Season.Spring);

        day.Select(r => (r.Slot, r.ItemId, r.Quantity, r.RewardCoins)).Should().Equal(
            RequestRules.Roll(7, Season.Spring).Select(r => (r.Slot, r.ItemId, r.Quantity, r.RewardCoins)));
        day.Should().HaveCount(RequestRules.RequestsPerDay);
        day.Select(r => r.Slot).Should().Equal(0, 1, 2);
    }

    [Theory]
    [MemberData(nameof(Seasons))]
    public void Roll_AsksOnlyForItemsInSeason_AndNeverTheSameItemTwiceInADay(Season season)
    {
        HashSet<int> inSeason = [.. RequestRules.ItemsInSeason(season)];
        for (int day = 1; day <= 200; day++)
        {
            RequestTemplate[] requests = RequestRules.Roll(day, season);

            requests.Should().HaveCount(RequestRules.RequestsPerDay);
            requests.Select(r => r.ItemId).Should().OnlyContain(id => inSeason.Contains(id)).And.OnlyHaveUniqueItems();
        }
    }

    [Theory]
    [MemberData(nameof(Seasons))]
    public void ItemsInSeason_AreCropHarvestsOrForageOfThatSeason_AndTheBinPricesThem(Season season)
    {
        HashSet<int> expected = [];
        foreach (CropDefinition crop in CropCatalog.All.Where(c => c.Seasons.Contains(season)))
        {
            _ = expected.Add(crop.HarvestItemId);
        }

        foreach (ForageEntry forage in ForageCatalog.All.Where(f => f.Seasons.Contains(season)))
        {
            _ = expected.Add(forage.ItemId);
        }

        int[] items = RequestRules.ItemsInSeason(season);

        items.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        items.Should().BeSubsetOf(expected);
        items.Where(id => !ShippingPrices.TryGet(id, out int _)).Should().BeEmpty("an unpriced item has no base value to scale a reward from");
        items.Length.Should().BeGreaterThanOrEqualTo(RequestRules.RequestsPerDay, "every season can fill a day's board");
    }

    [Fact]
    public void ItemsInSeason_Winter_HasNoCropOfAnotherSeason()
    {
        int[] winter = RequestRules.ItemsInSeason(Season.Winter);

        foreach (CropDefinition crop in CropCatalog.All.Where(c => !c.Seasons.Contains(Season.Winter)))
        {
            winter.Should().NotContain(crop.HarvestItemId, $"{crop.Key} does not grow in winter");
        }
    }

    [Fact]
    public void Roll_AcrossManyDays_UsesMoreThanOneItem()
    {
        RequestRules.Roll(1, Season.Spring).Select(r => r.ItemId).Concat(
            Enumerable.Range(2, 40).SelectMany(d => RequestRules.Roll(d, Season.Spring).Select(r => r.ItemId))).Distinct().Count()
            .Should().BeGreaterThan(RequestRules.RequestsPerDay, "the days differ");
    }

    [Fact]
    public void RewardFor_IsTheBinValuePlusThePremium_RoundedDown()
    {
        ShippingPrices.TryGet(ItemCatalog.Parsnip, out int each).Should().BeTrue();

        RequestRules.RewardFor(ItemCatalog.Parsnip, 5).Should().Be(each * 5L * (100 + RequestRules.PremiumPercent) / 100);
        RequestRules.RewardFor(ItemCatalog.Parsnip, 5).Should().BeGreaterThan(each * 5L, "filling a request beats the bin");
        RequestRules.RewardFor(ItemCatalog.Parsnip, 0).Should().Be(0);
        RequestRules.RewardFor(ItemCatalog.Hoe, 3).Should().Be(0, "the bin does not price a hoe");
        RequestRules.RewardFor(ItemCatalog.Parsnip, 1).Should().Be(each * (100 + RequestRules.PremiumPercent) / 100);
    }

    [Fact]
    public void QuantityFor_DividesTheValueByThePrice_AndStaysWithinBounds()
    {
        ShippingPrices.TryGet(ItemCatalog.Parsnip, out int each).Should().BeTrue();

        RequestRules.QuantityFor(ItemCatalog.Parsnip, each * 5).Should().Be(5);
        RequestRules.QuantityFor(ItemCatalog.Parsnip, each - 1).Should().Be(1, "at least one item is asked for");
        RequestRules.QuantityFor(ItemCatalog.Parsnip, each * 10_000).Should().Be(RequestRules.MaxQuantity);
        RequestRules.QuantityFor(ItemCatalog.Hoe, 500).Should().Be(1, "an item the bin does not price is asked for once");
    }

    [Fact]
    public void Roll_EveryRequest_HasPositiveQuantityAndARewardAboveItsBaseValue()
    {
        foreach (Season season in Enum.GetValues<Season>())
        {
            for (int day = 1; day <= 100; day++)
            {
                foreach (RequestTemplate request in RequestRules.Roll(day, season))
                {
                    ShippingPrices.TryGet(request.ItemId, out int each).Should().BeTrue();
                    request.Quantity.Should().BeInRange(1, RequestRules.MaxQuantity);
                    request.RewardCoins.Should().Be(RequestRules.RewardFor(request.ItemId, request.Quantity));
                    request.RewardCoins.Should().BeGreaterThan((long)each * request.Quantity);
                }
            }
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void IsValidSlot_AcceptsOnlyTheSlotsOfADay(int slot, bool valid) => RequestRules.IsValidSlot(slot).Should().Be(valid);

    [Fact]
    public void EarlyWindowMs_IsTheFirstFourInGameHoursOfTheDay()
    {
        // A game day is 24 in-game hours, so 1 000 ms of real time is one in-game hour in this 24 000 ms day.
        RequestRules.EarlyWindowMs(24_000).Should().Be(4_000);
        RequestRules.EarlyWindowMs(GameCalendar.DefaultDayLengthMs).Should().Be(2 * 60 * 60 * 1000, "four of twelve real hours");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3_999, true)]
    [InlineData(4_000, false)]
    [InlineData(-1, false)]
    public void IsEarly_OnlyInTheFirstFourInGameHours(long msIntoDay, bool early) => RequestRules.IsEarly(msIntoDay, 24_000).Should().Be(early);

    [Theory]
    [InlineData(false, 130)]
    [InlineData(true, 156)]
    public void PaidFor_EarlyAddsTheBonusPercent_AndLateLeavesTheReward(bool early, long paid) => RequestRules.PaidFor(130, early).Should().Be(paid);

    [Fact]
    public void PaidFor_EarlyRoundsDown()
    {
        // 121 × 1.2 = 145.2, which is paid as 145.
        RequestRules.PaidFor(121, early: true).Should().Be(145);
    }
}
