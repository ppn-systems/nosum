// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Gameplay.Text;
using Nosum.Gameplay.Trade;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The pure rules of the social layer: coins and fees, listings, the shipping bin, looks, trade confirmation, co-op names and withdrawals.</summary>
public sealed class SocialRulesTests
{
    private static readonly Guid s_a = Guid.CreateVersion7();
    private static readonly Guid s_b = Guid.CreateVersion7();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(100, 5)]
    [InlineData(999, 50)]
    [InlineData(1_000_000_000, 50_000_000)]
    public void MarketFee_IsFivePercentRoundedUp_SoAMicroListingPaysAtLeastItsShare(long price, long fee)
    {
        CoinRules.MarketFee(price).Should().Be(fee);
        CoinRules.SellerProceeds(price).Should().Be(price - fee);
    }

    [Fact]
    public void ValidateListing_AcceptedAndEachRefusal()
    {
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 10, 100, 0).Should().Be(MarketVerdict.Ok);
        MarketRules.ValidateListing(9999, 1, 100, 0).Should().Be(MarketVerdict.UnknownItem);
        MarketRules.ValidateListing(ItemCatalog.Hoe, 1, 100, 0).Should().Be(MarketVerdict.NotTradable);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 0, 100, 0).Should().Be(MarketVerdict.InvalidQuantity);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 100, 100, 0).Should().Be(MarketVerdict.InvalidQuantity, "a stack holds at most 99");
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 1, 0, 0).Should().Be(MarketVerdict.InvalidPrice);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 1, CoinRules.MaxListingPrice + 1, 0).Should().Be(MarketVerdict.InvalidPrice);
        MarketRules.ValidateListing(ItemCatalog.Parsnip, 1, 5, CoinRules.MaxActiveListings).Should().Be(MarketVerdict.TooManyListings);
    }

    [Fact]
    public void ShippingPrices_OnlyCropsAreBought()
    {
        ShippingPrices.TryGet(ItemCatalog.Cauliflower, out int coins).Should().BeTrue();
        coins.Should().BeGreaterThan(0);
        ShippingPrices.TryGet(ItemCatalog.ParsnipSeeds, out _).Should().BeFalse();
        ShippingPrices.TryGet(ItemCatalog.Hoe, out _).Should().BeFalse();
    }

    [Fact]
    public void ShippingBinRules_InReach_NextToTheBinOnly()
    {
        // The bin stands at tile (71, 14), two tiles wide.
        ShippingBinRules.InReach(72 * 16, 15 * 16 + 8, 16).Should().BeTrue();
        ShippingBinRules.InReach(10 * 16, 10 * 16, 16).Should().BeFalse();
    }

    [Fact]
    public void LookCatalog_ValidateAppearance_RejectsIdsOutsideTheCatalogue()
    {
        LookCatalog.ValidateAppearance(1, 1, 1).Should().Be(LookVerdict.Ok);
        LookCatalog.ValidateAppearance(0, 1, 1).Should().Be(LookVerdict.InvalidSkin);
        LookCatalog.ValidateAppearance(LookCatalog.SkinCount + 1, 1, 1).Should().Be(LookVerdict.InvalidSkin);
        LookCatalog.ValidateAppearance(1, LookCatalog.HairStyleCount + 1, 1).Should().Be(LookVerdict.InvalidHairStyle);
        LookCatalog.ValidateAppearance(1, 1, 0).Should().Be(LookVerdict.InvalidHairDye);
    }

    [Fact]
    public void PlayerLook_WithAndWorn_RoundTripPerSlot()
    {
        PlayerLook look = PlayerLook.Default.With(WearSlot.Hat, ItemCatalog.HatCap).With(WearSlot.Shirt, ItemCatalog.ShirtBlue);

        look.Worn(WearSlot.Hat).Should().Be(ItemCatalog.HatCap);
        look.Worn(WearSlot.Shirt).Should().Be(ItemCatalog.ShirtBlue);
        look.Worn(WearSlot.Pants).Should().Be(0);
    }

    [Fact]
    public void Clothing_ItemsAreWearableAndTradable_ToolsAreNot()
    {
        ItemCatalog.TryGet(ItemCatalog.HatStraw, out ItemDefinition? hat).Should().BeTrue();
        hat!.Wear.Should().Be(WearSlot.Hat);
        hat.IsTradable.Should().BeTrue();
        ItemCatalog.TryGet(ItemCatalog.Hoe, out ItemDefinition? hoe).Should().BeTrue();
        hoe!.IsTradable.Should().BeFalse();
    }

    [Fact]
    public void TradeSession_ChangingAnOfferClearsBothConfirmations()
    {
        TradeSession session = new(s_a, s_b);
        session.SetOffer(s_a, Offer(ItemCatalog.Parsnip, 5, 0)).Should().Be(TradeVerdict.Ok);
        session.SetOffer(s_b, Offer(ItemCatalog.Radish, 0, 100)).Should().Be(TradeVerdict.Ok);
        session.Confirm(s_a);
        session.Confirm(s_b);
        session.IsReady.Should().BeTrue();

        session.SetOffer(s_b, Offer(ItemCatalog.Radish, 0, 150));

        session.IsConfirmed(s_a).Should().BeFalse();
        session.IsConfirmed(s_b).Should().BeFalse();
        session.IsReady.Should().BeFalse();
    }

    [Fact]
    public void TradeSession_SettingTheSameOfferAgain_KeepsTheConfirmations()
    {
        TradeSession session = new(s_a, s_b);
        session.SetOffer(s_a, Offer(ItemCatalog.Parsnip, 5, 0));
        session.Confirm(s_a);

        session.SetOffer(s_a, Offer(ItemCatalog.Parsnip, 5, 0));

        session.IsConfirmed(s_a).Should().BeTrue();
    }

    [Fact]
    public void TradeSession_ReadyOnlyWhenBothConfirmedTheCurrentOffers()
    {
        TradeSession session = new(s_a, s_b);
        session.SetOffer(s_a, Offer(ItemCatalog.Parsnip, 1, 0));

        session.Confirm(s_a);
        session.IsReady.Should().BeFalse();
        session.Confirm(s_b);
        session.IsReady.Should().BeTrue();
    }

    [Fact]
    public void TradeSession_EmptyTradeCannotBeConfirmed_AndStrangersAndClosedSessionsAreRefused()
    {
        TradeSession session = new(s_a, s_b);

        session.Confirm(s_a).Should().Be(TradeVerdict.NothingOffered);
        session.SetOffer(Guid.NewGuid(), TradeOffer.Empty).Should().Be(TradeVerdict.NotParticipant);
        session.Close();
        session.SetOffer(s_a, Offer(ItemCatalog.Parsnip, 1, 0)).Should().Be(TradeVerdict.Closed);
        session.IsReady.Should().BeFalse();
    }

    [Fact]
    public void TradeOffer_Validate_RejectsUntradableDuplicatedOrNegative()
    {
        new TradeOffer([new TradeLine(ItemCatalog.Hoe, 1)], 0).Validate().Should().Be(TradeVerdict.InvalidOffer);
        new TradeOffer([new TradeLine(ItemCatalog.Parsnip, 1), new TradeLine(ItemCatalog.Parsnip, 2)], 0).Validate().Should().Be(TradeVerdict.InvalidOffer);
        new TradeOffer([new TradeLine(ItemCatalog.Parsnip, 0)], 0).Validate().Should().Be(TradeVerdict.InvalidOffer);
        new TradeOffer([], -1).Validate().Should().Be(TradeVerdict.InvalidOffer);
        new TradeOffer([new TradeLine(ItemCatalog.Parsnip, 3)], 10).Validate().Should().Be(TradeVerdict.Ok);
    }

    [Fact]
    public void TradeSession_SameLinesInAnotherOrder_AreTheSameOffer()
    {
        TradeOffer first = new([new TradeLine(1, 2), new TradeLine(3, 4)], 5);
        TradeOffer second = new([new TradeLine(3, 4), new TradeLine(1, 2)], 5);

        first.Equals(second).Should().BeTrue();
    }

    [Fact]
    public void CoopRules_Withdrawable_FounderTakesAllMemberOnlyTheirNetContribution()
    {
        CoopRules.Withdrawable(true, 50, 0).Should().Be(50);
        CoopRules.Withdrawable(false, 50, 20).Should().Be(20);
        CoopRules.Withdrawable(false, 10, 20).Should().Be(10);
        CoopRules.Withdrawable(false, 50, -5).Should().Be(0);
        CoopRules.CanWithdraw(false, 50, 20, 21).Should().Be(CoopVerdict.NotAllowed);
        CoopRules.CanWithdraw(false, 50, 20, 20).Should().Be(CoopVerdict.Ok);
        CoopRules.CanWithdraw(true, 50, 0, 0).Should().Be(CoopVerdict.NotAllowed);
    }

    [Fact]
    public void CoopRules_ValidateStack_RefusesToolsAndUnknownItems()
    {
        CoopRules.ValidateStack(ItemCatalog.Parsnip, 3).Should().Be(CoopVerdict.Ok);
        CoopRules.ValidateStack(ItemCatalog.Hoe, 1).Should().Be(CoopVerdict.NotTradable);
        CoopRules.ValidateStack(12345, 1).Should().Be(CoopVerdict.UnknownItem);
        CoopRules.ValidateStack(ItemCatalog.Parsnip, 0).Should().Be(CoopVerdict.InvalidQuantity);
    }

    [Theory]
    [InlineData("Green Acres", true)]
    [InlineData("ab", false)]
    [InlineData("   ", false)]
    [InlineData("tab\there", false)]
    [InlineData("i18n{\"vi\":\"Vườn Xanh\",\"en\":\"Green Garden\"}", true)]
    [InlineData("i18n{\"vi\":\"Vườn Xanh\",", false)]
    [InlineData("i18n{\"vi\":\"x\"}", false)]
    [InlineData("i18n{\"vi\":5}", false)]
    [InlineData("i18n{}", false)]
    public void CoopRules_ValidateName_AcceptsPlainTextAndWellFormedEnvelopes(string name, bool valid)
    {
        (CoopRules.ValidateName(name) == CoopVerdict.Ok).Should().Be(valid);
    }

    [Fact]
    public void LocalizedText_Resolve_ExactThenBaseThenEnglishThenFirst()
    {
        const string envelope = "i18n{\"vi\":\"Xin chào\",\"en\":\"Hello\",\"fr\":\"Bonjour\"}";

        LocalizedText.Resolve(envelope, "vi").Should().Be("Xin chào");
        LocalizedText.Resolve(envelope, "en-US").Should().Be("Hello");
        LocalizedText.Resolve(envelope, "de").Should().Be("Hello");
        LocalizedText.Resolve("i18n{\"fr\":\"Bonjour\"}", "de").Should().Be("Bonjour");
        LocalizedText.Resolve("plain", "vi").Should().Be("plain");
    }

    private static TradeOffer Offer(int itemId, int quantity, long coins)
        => new(quantity > 0 ? new List<TradeLine> { new(itemId, quantity) } : [], coins);
}
