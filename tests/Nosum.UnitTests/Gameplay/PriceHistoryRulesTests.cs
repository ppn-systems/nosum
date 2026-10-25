// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The market's anti-cornering limit and the price summary of a day's sales (docs/adr/ADR-030 D4).</summary>
public sealed class PriceHistoryRulesTests
{
    [Fact]
    public void ValidateListing_UpTo500UnitsOfOneItemInAll_IsAllowed_Past500IsRefused()
    {
        CoinRules.MaxListedUnitsPerItem.Should().Be(500);

        MarketRules.ValidateListing(ItemCatalog.Parsnip, 99, 100, 3, 401).Should().Be(MarketVerdict.Ok, "401 + 99 = 500");
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 99, 100, 3, 402).Should().Be(MarketVerdict.TooManyOfItem, "402 + 99 = 501");
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 1, 100, 0, 500).Should().Be(MarketVerdict.TooManyOfItem);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 1, 100, 0, 499).Should().Be(MarketVerdict.Ok);
    }

    [Fact]
    public void ValidateListing_TheOtherRefusals_StillComeFirst()
    {
        MarketRules.ValidateListing(9999, 1, 100, 0, 10_000).Should().Be(MarketVerdict.UnknownItem);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 0, 100, 0, 10_000).Should().Be(MarketVerdict.InvalidQuantity);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 1, 100, CoinRules.MaxActiveListings, 10_000).Should().Be(MarketVerdict.TooManyListings);
    }

    [Fact]
    public void ValidateListing_WithoutTheUnitsArgument_BehavesAsBefore()
        => MarketRules.ValidateListing(ItemCatalog.Parsnip, 10, 100, 0).Should().Be(MarketVerdict.Ok);

    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(105, 10, 11)]
    [InlineData(104, 10, 10)]
    [InlineData(1, 99, 1)]
    [InlineData(7, 1, 7)]
    public void UnitPrice_IsTheStackPriceDividedByTheQuantity_RoundedToTheNearestCoin_AtLeastOne(long stack, int quantity, long expected)
        => PriceHistoryRules.UnitPrice(stack, quantity).Should().Be(expected);

    [Fact]
    public void UnitPrice_ZeroQuantity_Throws()
    {
        Action act = () => PriceHistoryRules.UnitPrice(10, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Summarize_OneSale_AllThreeAreThatPrice()
        => PriceHistoryRules.Summarize([42]).Should().Be(new PriceSummary(42, 42, 42, 1));

    [Fact]
    public void Summarize_OddCount_MedianIsTheMiddleOne_RegardlessOfOrder()
        => PriceHistoryRules.Summarize([30, 10, 50, 20, 40]).Should().Be(new PriceSummary(10, 30, 50, 5));

    [Fact]
    public void Summarize_EvenCount_MedianIsTheMeanOfTheMiddleTwo_RoundedDown()
    {
        PriceHistoryRules.Summarize([10, 20, 30, 40]).Should().Be(new PriceSummary(10, 25, 40, 4));
        PriceHistoryRules.Summarize([10, 21]).Median.Should().Be(15);
    }

    [Fact]
    public void Summarize_Nothing_Throws()
    {
        Action act = () => PriceHistoryRules.Summarize([]);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SeasonNumberAt_CountsSeasonsSinceTheEpochWithoutEnd()
    {
        GameCalendar calendar = new(0, GameCalendar.DefaultDayLengthMs);
        long season = calendar.SeasonLengthMs;

        calendar.SeasonNumberAt(-5).Should().Be(0);
        calendar.SeasonNumberAt(0).Should().Be(0);
        calendar.SeasonNumberAt(season - 1).Should().Be(0);
        calendar.SeasonNumberAt(season).Should().Be(1);
        calendar.SeasonNumberAt((season * 4) + 1).Should().Be(4, "the second spring is season number 4, a different row than the first");
        calendar.SeasonAt((season * 4) + 1).Should().Be(Season.Spring);
    }
}
