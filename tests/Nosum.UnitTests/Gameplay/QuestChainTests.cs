// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Quests;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The tutorial chain (docs/adr/ADR-036 D1-D3): every quest is in the chain once, ids are frozen, each step counts exactly the event it names and pays a reward.</summary>
public sealed class QuestChainTests
{
    [Fact]
    public void Chain_HoldsEveryQuestExactlyOnce_InTheDocumentedOrder()
    {
        QuestCatalog.Chain.Should().OnlyHaveUniqueItems();
        QuestCatalog.Chain.Should().BeEquivalentTo(QuestCatalog.All.Select(q => q.Id));
        QuestCatalog.Chain.Should().Equal(
            QuestCatalog.TillSoil,
            QuestCatalog.WaterCrop,
            QuestCatalog.FirstHarvest,
            QuestCatalog.ShipProduce,
            QuestCatalog.BuySeeds,
            QuestCatalog.ChopTree,
            QuestCatalog.CraftChest,
            QuestCatalog.CraftRod,
            QuestCatalog.CatchFish,
            QuestCatalog.MineOre,
            QuestCatalog.SlaySlime,
            QuestCatalog.UpgradeTool,
            QuestCatalog.BuildCoop,
            QuestCatalog.BuyAnimal,
            QuestCatalog.CookMeal,
            QuestCatalog.EatFood,
            QuestCatalog.FillRequest,
            QuestCatalog.Decorate,
            QuestCatalog.AdoptPet,
            QuestCatalog.SendEmote);
    }

    [Fact]
    public void Chain_IsTwentySteps_SoEveryStepHasItsOwnPlace()
    {
        QuestCatalog.Chain.Should().HaveCount(20);
        QuestCatalog.All.Should().HaveCount(20);
    }

    [Fact]
    public void QuestIds_AreFrozen_BecauseTheyAreStoredAndSent()
    {
        QuestCatalog.FirstHarvest.Should().Be(1);
        QuestCatalog.TillSoil.Should().Be(2);
        QuestCatalog.WaterCrop.Should().Be(3);
        QuestCatalog.ShipProduce.Should().Be(4);
        QuestCatalog.BuySeeds.Should().Be(5);
        QuestCatalog.ChopTree.Should().Be(6);
        QuestCatalog.CraftChest.Should().Be(7);
        QuestCatalog.CraftRod.Should().Be(8);
        QuestCatalog.CatchFish.Should().Be(9);
        QuestCatalog.MineOre.Should().Be(10);
        QuestCatalog.SlaySlime.Should().Be(11);
        QuestCatalog.UpgradeTool.Should().Be(12);
        QuestCatalog.BuildCoop.Should().Be(13);
        QuestCatalog.BuyAnimal.Should().Be(14);
        QuestCatalog.CookMeal.Should().Be(15);
        QuestCatalog.EatFood.Should().Be(16);
        QuestCatalog.FillRequest.Should().Be(17);
        QuestCatalog.Decorate.Should().Be(18);
        QuestCatalog.AdoptPet.Should().Be(19);
        QuestCatalog.SendEmote.Should().Be(20);
    }

    [Fact]
    public void FirstHarvest_KeepsItsTwoObjectivesInTheirOrder_SoStoredProgressStaysValid()
    {
        QuestCatalog.TryGet(QuestCatalog.FirstHarvest, out QuestDefinition? quest).Should().BeTrue();

        quest!.ObjectiveCount.Should().Be(2);
        quest.ObjectiveAt(0).Kind.Should().Be(QuestEventKind.CropPlanted);
        quest.ObjectiveAt(1).Kind.Should().Be(QuestEventKind.CropHarvested);
    }

    [Fact]
    public void EveryQuest_PaysACoinReward()
    {
        QuestCatalog.All.Should().OnlyContain(q => q.RewardCoins > 0);
    }

    [Fact]
    public void EveryNewStep_PaysAModestReward_BalancedAgainstTheShopPrices()
    {
        // The new steps are small rewards: between one emote's worth (10) and the most expensive step (80), not the price of a shop item (docs/adr/ADR-036 D2).
        QuestCatalog.All.Where(q => q.Id > QuestCatalog.CraftChest).Should().OnlyContain(q => q.RewardCoins >= 10 && q.RewardCoins <= 80);
    }

    [Fact]
    public void PositionOf_FollowsTheChain_AndSortsUnknownQuestsLast()
    {
        QuestCatalog.PositionOf(QuestCatalog.TillSoil).Should().Be(0);
        QuestCatalog.PositionOf(QuestCatalog.FirstHarvest).Should().Be(2);
        QuestCatalog.PositionOf(QuestCatalog.CraftChest).Should().Be(6);
        QuestCatalog.PositionOf(QuestCatalog.CraftRod).Should().Be(7);
        QuestCatalog.PositionOf(QuestCatalog.SendEmote).Should().Be(QuestCatalog.Chain.Count - 1);
        QuestCatalog.PositionOf(999).Should().Be(int.MaxValue);
    }

    [Theory]
    [InlineData(QuestCatalog.TillSoil, QuestEventKind.SoilTilled, 0)]
    [InlineData(QuestCatalog.WaterCrop, QuestEventKind.CropWatered, 0)]
    [InlineData(QuestCatalog.ShipProduce, QuestEventKind.ItemShipped, ItemCatalog.Parsnip)]
    [InlineData(QuestCatalog.BuySeeds, QuestEventKind.SeedBought, ItemCatalog.ParsnipSeeds)]
    [InlineData(QuestCatalog.ChopTree, QuestEventKind.NodeBroken, (int)NodeKind.Tree)]
    [InlineData(QuestCatalog.CraftChest, QuestEventKind.ItemCrafted, ItemCatalog.Chest)]
    [InlineData(QuestCatalog.CraftRod, QuestEventKind.ItemCrafted, ItemCatalog.FishingRod)]
    [InlineData(QuestCatalog.CatchFish, QuestEventKind.FishCaught, ItemCatalog.Carp)]
    [InlineData(QuestCatalog.UpgradeTool, QuestEventKind.ToolUpgraded, ItemCatalog.CopperAxe)]
    [InlineData(QuestCatalog.BuildCoop, QuestEventKind.BuildingPlaced, ItemCatalog.CoopBlueprint)]
    [InlineData(QuestCatalog.BuyAnimal, QuestEventKind.AnimalBought, 1)]
    [InlineData(QuestCatalog.CookMeal, QuestEventKind.ItemCrafted, ItemCatalog.Salad)]
    [InlineData(QuestCatalog.EatFood, QuestEventKind.FoodEaten, ItemCatalog.Salad)]
    [InlineData(QuestCatalog.FillRequest, QuestEventKind.RequestFilled, ItemCatalog.Parsnip)]
    [InlineData(QuestCatalog.AdoptPet, QuestEventKind.PetAdopted, 1)]
    [InlineData(QuestCatalog.SendEmote, QuestEventKind.EmoteSent, 0)]
    public void EachSingleStepQuest_CompletesOnItsOwnEvent(int questId, QuestEventKind kind, int subject)
    {
        QuestCatalog.TryGet(questId, out QuestDefinition? quest).Should().BeTrue();
        int[] progress = new int[quest!.ObjectiveCount];

        quest.Apply(progress, new QuestEvent(kind, subject)).Should().BeTrue();

        quest.IsComplete(progress).Should().BeTrue();
    }

    [Theory]
    [InlineData(QuestCatalog.MineOre, QuestEventKind.NodeBroken, (int)NodeKind.CopperOre, 3)]
    [InlineData(QuestCatalog.SlaySlime, QuestEventKind.MonsterDefeated, 1, 3)]
    [InlineData(QuestCatalog.Decorate, QuestEventKind.PlaceablePlaced, ItemCatalog.Chest, 3)]
    public void EachCountedStep_NeedsItsWholeCount_AndStopsCountingAtIt(int questId, QuestEventKind kind, int subject, int count)
    {
        QuestCatalog.TryGet(questId, out QuestDefinition? quest).Should().BeTrue();
        int[] progress = new int[quest!.ObjectiveCount];

        for (int i = 1; i < count; i++)
        {
            quest.Apply(progress, new QuestEvent(kind, subject));
            quest.IsComplete(progress).Should().BeFalse("only {0} of {1} so far", i, count);
        }

        quest.Apply(progress, new QuestEvent(kind, subject));
        quest.IsComplete(progress).Should().BeTrue();
        quest.Apply(progress, new QuestEvent(kind, subject)).Should().BeFalse("a finished objective stops counting");
    }

    [Theory]
    [InlineData(QuestCatalog.CraftChest, QuestEventKind.ItemCrafted, ItemCatalog.Furnace)]
    [InlineData(QuestCatalog.CraftRod, QuestEventKind.ItemCrafted, ItemCatalog.Chest)]
    [InlineData(QuestCatalog.MineOre, QuestEventKind.NodeBroken, (int)NodeKind.Stone)]
    [InlineData(QuestCatalog.MineOre, QuestEventKind.NodeBroken, (int)NodeKind.Tree)]
    [InlineData(QuestCatalog.BuildCoop, QuestEventKind.BuildingPlaced, ItemCatalog.BarnBlueprint)]
    [InlineData(QuestCatalog.CookMeal, QuestEventKind.ItemCrafted, ItemCatalog.Chest)]
    [InlineData(QuestCatalog.ChopTree, QuestEventKind.NodeBroken, (int)NodeKind.Stone)]
    public void ASubjectSpecificStep_IgnoresOtherSubjects(int questId, QuestEventKind kind, int subject)
    {
        QuestCatalog.TryGet(questId, out QuestDefinition? quest).Should().BeTrue();
        int[] progress = new int[quest!.ObjectiveCount];

        quest.Apply(progress, new QuestEvent(kind, subject)).Should().BeFalse();

        quest.IsComplete(progress).Should().BeFalse();
    }

    [Fact]
    public void EventKinds_AreAppendOnly()
    {
        ((int)QuestEventKind.CropPlanted).Should().Be(1);
        ((int)QuestEventKind.CropHarvested).Should().Be(2);
        ((int)QuestEventKind.SoilTilled).Should().Be(3);
        ((int)QuestEventKind.CropWatered).Should().Be(4);
        ((int)QuestEventKind.ItemShipped).Should().Be(5);
        ((int)QuestEventKind.SeedBought).Should().Be(6);
        ((int)QuestEventKind.NodeBroken).Should().Be(7);
        ((int)QuestEventKind.ItemCrafted).Should().Be(8);
        ((int)QuestEventKind.FishCaught).Should().Be(9);
        ((int)QuestEventKind.MonsterDefeated).Should().Be(10);
        ((int)QuestEventKind.ToolUpgraded).Should().Be(11);
        ((int)QuestEventKind.BuildingPlaced).Should().Be(12);
        ((int)QuestEventKind.AnimalBought).Should().Be(13);
        ((int)QuestEventKind.FoodEaten).Should().Be(14);
        ((int)QuestEventKind.RequestFilled).Should().Be(15);
        ((int)QuestEventKind.PlaceablePlaced).Should().Be(16);
        ((int)QuestEventKind.PetAdopted).Should().Be(17);
        ((int)QuestEventKind.EmoteSent).Should().Be(18);
    }
}
