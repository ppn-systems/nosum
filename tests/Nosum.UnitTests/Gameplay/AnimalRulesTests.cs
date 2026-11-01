// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Feeding, produce, petting and friendship of farm animals (docs/adr/ADR-028 D3).</summary>
public sealed class AnimalRulesTests
{
    private static readonly Guid s_animal = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Theory]
    [InlineData(ItemCatalog.Hay, 1)]
    [InlineData(ItemCatalog.Fiber, 1)]
    [InlineData(ItemCatalog.Wood, 0)]
    [InlineData(ItemCatalog.Parsnip, 0)]
    public void RationsOf_OnlyHayAndFiberFeedAnimals(int itemId, int expected)
    {
        AnimalRules.RationsOf(itemId).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 2, true)]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 0, false)]
    public void BenchNeedsHay_EmptyBenchOnlyWhileAnimalsLive(int rations, int animals, bool expected)
    {
        AnimalRules.BenchNeedsHay(rations, animals).Should().Be(expected);
    }

    [Fact]
    public void MaxFeedAtOnce_CoversTheWholeBench()
    {
        // The trough feeds up to the bench's size in one press (PlayerActions.TendAnimals); a smaller number would need several presses for an empty bench.
        AnimalRules.MaxFeedAtOnce.Should().Be(AnimalRules.MaxRations);
        AnimalRules.MaxRations.Should().BeGreaterThan(4, "the bench holds more than one animal's capacity (four)");
    }

    [Fact]
    public void Settle_SameDay_ChangesNothing()
    {
        AnimalRules.Settle(5, 3, 7, 7).Should().Be(new FeedingSettlement(5, 0));
        AnimalRules.Settle(5, 3, 7, 6).Should().Be(new FeedingSettlement(5, 0));
    }

    [Fact]
    public void Settle_NextDayWithEnoughHay_FeedsEveryAnimalOneRation()
    {
        AnimalRules.Settle(5, 3, 7, 8).Should().Be(new FeedingSettlement(2, 3));
    }

    [Fact]
    public void Settle_NotEnoughHay_FeedsOnlyAsManyAnimalsAsRations()
    {
        AnimalRules.Settle(2, 4, 7, 8).Should().Be(new FeedingSettlement(0, 2));
    }

    [Fact]
    public void Settle_NoHay_FeedsNobody()
    {
        AnimalRules.Settle(0, 4, 7, 8).Should().Be(new FeedingSettlement(0, 0));
    }

    [Fact]
    public void Settle_SeveralNightsAway_EatsOneRationPerAnimalPerNight()
    {
        // 7 rations, 2 animals: nights 1-3 take 2 each (1 left), night 4 takes the last one.
        AnimalRules.Settle(7, 2, 10, 14).Should().Be(new FeedingSettlement(0, 1));
        AnimalRules.Settle(7, 2, 10, 13).Should().Be(new FeedingSettlement(1, 2));
    }

    [Fact]
    public void Settle_HayRanOutBeforeToday_FeedsNobodyToday()
    {
        AnimalRules.Settle(4, 2, 10, 15).Should().Be(new FeedingSettlement(0, 0));
    }

    [Fact]
    public void Settle_NoAnimals_KeepsTheHay()
    {
        AnimalRules.Settle(6, 0, 1, 9).Should().Be(new FeedingSettlement(6, 0));
    }

    [Theory]
    [InlineData(8, 0, 8, true)]
    [InlineData(8, 8, 8, false)]
    [InlineData(7, 0, 8, false)]
    [InlineData(AnimalRules.Never, AnimalRules.Never, 8, false)]
    public void HasProduce_OnlyOnTheDayItWasMade_AndOnlyUntilCollected(int produceDay, int collectedDay, int today, bool expected)
    {
        AnimalRules.HasProduce(produceDay, collectedDay, today).Should().Be(expected);
    }

    [Fact]
    public void CanPet_OncePerDay()
    {
        AnimalRules.CanPet(7, 8).Should().BeTrue();
        AnimalRules.CanPet(8, 8).Should().BeFalse();
        AnimalRules.CanPet(AnimalRules.Never, 1).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(500, 540)]
    [InlineData(980, 1000)]
    [InlineData(1000, 1000)]
    public void AfterPetting_RaisesFriendshipUpToTheMaximum(int before, int expected)
    {
        AnimalRules.AfterPetting(before).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(200, 15)]
    [InlineData(1000, 55)]
    [InlineData(5000, 55)]
    [InlineData(-3, 5)]
    public void LargeChancePercent_GrowsWithFriendship(int friendship, int expected)
    {
        AnimalRules.LargeChancePercent(friendship).Should().Be(expected);
    }

    [Fact]
    public void IsLarge_SameAnimalSameDay_AlwaysGivesTheSameAnswer()
    {
        for (int friendship = 0; friendship <= AnimalRules.MaxFriendship; friendship += 100)
        {
            AnimalRules.IsLarge(s_animal, 12, friendship).Should().Be(AnimalRules.IsLarge(s_animal, 12, friendship));
        }
    }

    [Fact]
    public void IsLarge_HigherFriendship_CanOnlyTurnSmallIntoLarge()
    {
        for (int day = 1; day <= 200; day++)
        {
            bool atZero = AnimalRules.IsLarge(s_animal, day, 0);
            bool atMax = AnimalRules.IsLarge(s_animal, day, AnimalRules.MaxFriendship);
            (atZero && !atMax).Should().BeFalse($"day {day}: a large produce stays large");
        }
    }

    [Fact]
    public void IsLarge_OverManyDays_FollowsTheChance()
    {
        int largeAtZero = 0;
        int largeAtMax = 0;
        for (int day = 1; day <= 2_000; day++)
        {
            largeAtZero += AnimalRules.IsLarge(s_animal, day, 0) ? 1 : 0;
            largeAtMax += AnimalRules.IsLarge(s_animal, day, AnimalRules.MaxFriendship) ? 1 : 0;
        }

        largeAtZero.Should().BeInRange(60, 140, "5 percent of 2000 days");
        largeAtMax.Should().BeInRange(1_000, 1_200, "55 percent of 2000 days");
    }

    [Fact]
    public void ProduceOf_ChickenGivesEggs_CowGivesMilk()
    {
        _ = AnimalCatalog.TryGet(AnimalKind.Chicken, out AnimalDefinition? chicken).Should().BeTrue();
        _ = AnimalCatalog.TryGet(AnimalKind.Cow, out AnimalDefinition? cow).Should().BeTrue();

        AnimalRules.ProduceOf(chicken!, s_animal, 3, 0).Should().BeOneOf(ItemCatalog.Egg, ItemCatalog.LargeEgg);
        AnimalRules.ProduceOf(cow!, s_animal, 3, 0).Should().BeOneOf(ItemCatalog.Milk, ItemCatalog.LargeMilk);
        chicken!.NeedsPail.Should().BeFalse();
        cow!.NeedsPail.Should().BeTrue();
        (chicken.Coins, cow.Coins).Should().Be((800L, 1_500L));
    }

    [Fact]
    public void ProduceItems_AreEdibleProduceThatShipsForCoins()
    {
        foreach (int itemId in new[] { ItemCatalog.Egg, ItemCatalog.LargeEgg, ItemCatalog.Milk, ItemCatalog.LargeMilk, ItemCatalog.GoldenEgg, ItemCatalog.CreamMilk })
        {
            ItemCatalog.TryGet(itemId, out ItemDefinition? item).Should().BeTrue();
            item!.Kind.Should().Be(ItemKind.Produce);
            item.IsEdible.Should().BeTrue();
            ShippingPrices.TryGet(itemId, out int price).Should().BeTrue();
            price.Should().BePositive();
        }

        ShippingPrices.TryGet(ItemCatalog.LargeEgg, out int large).Should().BeTrue();
        ShippingPrices.TryGet(ItemCatalog.Egg, out int small).Should().BeTrue();
        large.Should().BeGreaterThan(small);
    }

    [Fact]
    public void GoldProduce_SellsForHalfMoreThanTheEverydayKind()
    {
        ShippingPrices.TryGet(ItemCatalog.GoldenEgg, out int goldEgg).Should().BeTrue();
        ShippingPrices.TryGet(ItemCatalog.CreamMilk, out int goldMilk).Should().BeTrue();
        ShippingPrices.TryGet(ItemCatalog.Egg, out int egg).Should().BeTrue();
        ShippingPrices.TryGet(ItemCatalog.Milk, out int milk).Should().BeTrue();

        goldEgg.Should().Be(egg * 3 / 2);
        goldMilk.Should().Be(milk * 3 / 2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(199, 0)]
    [InlineData(200, 1)]
    [InlineData(799, 3)]
    [InlineData(800, 4)]
    [InlineData(1000, 5)]
    [InlineData(5000, 5)]
    [InlineData(-5, 0)]
    public void HeartsOf_OneHeartPerTwoHundredFriendship(int friendship, int expected)
    {
        AnimalRules.HeartsOf(friendship).Should().Be(expected);
    }

    [Fact]
    public void IsGold_BelowFourHearts_IsNeverGold()
    {
        for (int day = 1; day <= 500; day++)
        {
            AnimalRules.IsGold(s_animal, day, AnimalRules.GoldFriendship - 1).Should().BeFalse();
        }
    }

    [Fact]
    public void IsGold_SameAnimalSameDay_AlwaysGivesTheSameAnswer()
    {
        bool first = AnimalRules.IsGold(s_animal, 42, AnimalRules.MaxFriendship);

        for (int i = 0; i < 10; i++)
        {
            AnimalRules.IsGold(s_animal, 42, AnimalRules.MaxFriendship).Should().Be(first);
        }
    }

    [Fact]
    public void IsGold_FourHeartsOverManyDays_FollowsTheChance()
    {
        int gold = 0;
        const int days = 10_000;
        for (int day = 1; day <= days; day++)
        {
            if (AnimalRules.IsGold(s_animal, day, AnimalRules.GoldFriendship))
            {
                gold++;
            }
        }

        double share = gold / (double)days;
        share.Should().BeInRange(0.22, 0.28);
    }

    [Fact]
    public void ProduceOf_FourHearts_CanGiveGoldProduce_AndNeverBelowFourHearts()
    {
        _ = AnimalCatalog.TryGet(AnimalKind.Chicken, out AnimalDefinition? chicken).Should().BeTrue();

        int[] atFour = Enumerable.Range(1, 500).Select(day => AnimalRules.ProduceOf(chicken!, s_animal, day, AnimalRules.GoldFriendship)).ToArray();
        int[] belowFour = Enumerable.Range(1, 500).Select(day => AnimalRules.ProduceOf(chicken!, s_animal, day, AnimalRules.GoldFriendship - 1)).ToArray();

        atFour.Should().Contain(ItemCatalog.GoldenEgg);
        belowFour.Should().NotContain(ItemCatalog.GoldenEgg);
    }

    [Fact]
    public void ProduceOf_GoldWinsOverLarge_WhenTheGoldRollHits()
    {
        _ = AnimalCatalog.TryGet(AnimalKind.Cow, out AnimalDefinition? cow).Should().BeTrue();

        for (int day = 1; day <= 500; day++)
        {
            if (AnimalRules.IsGold(s_animal, day, AnimalRules.MaxFriendship))
            {
                AnimalRules.ProduceOf(cow!, s_animal, day, AnimalRules.MaxFriendship).Should().Be(ItemCatalog.CreamMilk);
            }
        }
    }

    [Theory]
    [InlineData("Bella", true)]
    [InlineData("Mi Mi", true)]
    [InlineData("ab", false)]
    [InlineData("", false)]
    [InlineData("Admin", false)]
    [InlineData("Bad!Name", false)]
    public void IsValidName_FollowsTheNicknameRules(string name, bool expected)
    {
        AnimalRules.IsValidName(name).Should().Be(expected);
    }

    [Fact]
    public void IsValidName_ProfanityIsRefused()
    {
        AnimalRules.IsValidName("fuck").Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(0, 0, 44, 0, true)]
    [InlineData(0, 0, 45, 0, false)]
    [InlineData(0, 0, 40, 40, false)]
    public void InReach_IsADistanceCheck(double feetX, double feetY, double animalX, double animalY, bool expected)
    {
        AnimalRules.InReach(feetX, feetY, animalX, animalY).Should().Be(expected);
    }

    [Theory]
    [InlineData(ProduceSchedule.Daily, 7, false, true)]
    [InlineData(ProduceSchedule.EveryThirdDay, 9, false, true)]
    [InlineData(ProduceSchedule.EveryThirdDay, 10, true, false)]
    [InlineData(ProduceSchedule.SunnyDays, 10, true, true)]
    [InlineData(ProduceSchedule.SunnyDays, 10, false, false)]
    public void ScheduleAllows_FollowsTheSpeciesRhythmAndTheWeather(ProduceSchedule schedule, int today, bool sunny, bool expected)
    {
        AnimalRules.ScheduleAllows(schedule, today, sunny).Should().Be(expected);
    }

    [Fact]
    public void Yields_NeedsFeedingTodayAndTheScheduleToo()
    {
        _ = AnimalCatalog.TryGet(AnimalKind.Pig, out AnimalDefinition? pig).Should().BeTrue();
        _ = AnimalCatalog.TryGet(AnimalKind.Sheep, out AnimalDefinition? sheep).Should().BeTrue();

        AnimalRules.Yields(pig!, produceDay: 10, collectedDay: 0, today: 10, sunny: true).Should().BeTrue("fed yesterday, sunny, not collected");
        AnimalRules.Yields(pig!, produceDay: 10, collectedDay: 0, today: 10, sunny: false).Should().BeFalse("a pig digs only on sunny days");
        AnimalRules.Yields(pig!, produceDay: 10, collectedDay: 10, today: 10, sunny: true).Should().BeFalse("already collected");
        AnimalRules.Yields(pig!, produceDay: 9, collectedDay: 0, today: 10, sunny: true).Should().BeFalse("not fed the night before");
        AnimalRules.Yields(sheep!, produceDay: 10, collectedDay: 0, today: 10, sunny: true).Should().BeFalse("a sheep is shorn every third day only");
        AnimalRules.Yields(sheep!, produceDay: 9, collectedDay: 0, today: 9, sunny: true).Should().BeTrue("9 is a third day");
    }

    [Fact]
    public void Catalog_HasTheSixSpeciesWithTheirProduce()
    {
        AnimalCatalog.All.Select(a => a.Kind).Should().Equal(AnimalKind.Chicken, AnimalKind.Cow, AnimalKind.Duck, AnimalKind.Goat, AnimalKind.Sheep, AnimalKind.Pig);
        _ = AnimalCatalog.TryGet(AnimalKind.Duck, out AnimalDefinition? duck).Should().BeTrue();
        (duck!.Product, duck.NeedsPail).Should().Be((ItemCatalog.DuckEgg, false));
        _ = AnimalCatalog.TryGet(AnimalKind.Goat, out AnimalDefinition? goat).Should().BeTrue();
        (goat!.Product, goat.NeedsPail).Should().Be((ItemCatalog.GoatMilk, true));
        _ = AnimalCatalog.TryGet(AnimalKind.Sheep, out AnimalDefinition? sheep).Should().BeTrue();
        (sheep!.Product, sheep.Schedule).Should().Be((ItemCatalog.Wool, ProduceSchedule.EveryThirdDay));
        _ = AnimalCatalog.TryGet(AnimalKind.Pig, out AnimalDefinition? pig).Should().BeTrue();
        (pig!.Product, pig.Schedule).Should().Be((ItemCatalog.Truffle, ProduceSchedule.SunnyDays));
    }

    [Fact]
    public void Wool_IsUsableInTheWoolScarecrowRecipe()
    {
        RecipeCatalog.TryGet(RecipeCatalog.ScarecrowWool, out RecipeDefinition? recipe).Should().BeTrue();
        recipe!.Inputs.Should().Contain(i => i.ItemId == ItemCatalog.Wool && i.Quantity == 5);
        recipe.OutputItemId.Should().Be(ItemCatalog.Scarecrow);
    }

    [Theory]
    [InlineData(true, true, true, false, false, false, AnimalTend.Feed)]
    [InlineData(true, false, false, false, false, false, AnimalTend.Feed)]
    [InlineData(false, false, false, false, false, false, AnimalTend.None)]
    [InlineData(false, true, true, false, false, false, AnimalTend.Collect)]
    [InlineData(false, true, false, false, false, false, AnimalTend.Pet)]
    [InlineData(false, true, true, true, false, false, AnimalTend.Pet)]
    [InlineData(false, true, true, true, true, false, AnimalTend.Collect)]
    [InlineData(false, true, true, true, false, true, AnimalTend.Collect)]
    [InlineData(false, true, false, true, true, true, AnimalTend.Pet)]
    public void TendOf_SaysWhatTheActionKeyDoes(bool feedAtTrough, bool inReach, bool produce, bool needsPail, bool pail, bool petted, AnimalTend expected)
    {
        AnimalRules.TendOf(feedAtTrough, inReach, produce, needsPail, pail, petted).Should().Be(expected);
    }

    [Fact]
    public void TendOf_FeedWinsOverAnAnimalInReach()
    {
        AnimalRules.TendOf(feedAtTrough: true, animalInReach: true, hasProduce: true, needsPail: false, pailInHand: false, petted: false).Should().Be(AnimalTend.Feed);
    }
}

