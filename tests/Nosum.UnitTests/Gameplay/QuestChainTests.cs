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
            QuestCatalog.CraftChest);
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
    public void PositionOf_FollowsTheChain_AndSortsUnknownQuestsLast()
    {
        QuestCatalog.PositionOf(QuestCatalog.TillSoil).Should().Be(0);
        QuestCatalog.PositionOf(QuestCatalog.FirstHarvest).Should().Be(2);
        QuestCatalog.PositionOf(QuestCatalog.CraftChest).Should().Be(QuestCatalog.Chain.Count - 1);
        QuestCatalog.PositionOf(999).Should().Be(int.MaxValue);
    }

    [Theory]
    [InlineData(QuestCatalog.TillSoil, QuestEventKind.SoilTilled, 0)]
    [InlineData(QuestCatalog.WaterCrop, QuestEventKind.CropWatered, 0)]
    [InlineData(QuestCatalog.ShipProduce, QuestEventKind.ItemShipped, ItemCatalog.Parsnip)]
    [InlineData(QuestCatalog.BuySeeds, QuestEventKind.ItemBought, ItemCatalog.ParsnipSeeds)]
    [InlineData(QuestCatalog.ChopTree, QuestEventKind.NodeBroken, (int)NodeKind.Tree)]
    [InlineData(QuestCatalog.CraftChest, QuestEventKind.ItemCrafted, ItemCatalog.Chest)]
    public void EachSingleStepQuest_CompletesOnItsOwnEvent(int questId, QuestEventKind kind, int subject)
    {
        QuestCatalog.TryGet(questId, out QuestDefinition? quest).Should().BeTrue();
        int[] progress = new int[quest!.ObjectiveCount];

        quest.Apply(progress, new QuestEvent(kind, subject)).Should().BeTrue();

        quest.IsComplete(progress).Should().BeTrue();
    }

    [Theory]
    [InlineData(QuestCatalog.ChopTree, QuestEventKind.NodeBroken, (int)NodeKind.Stone)]
    [InlineData(QuestCatalog.CraftChest, QuestEventKind.ItemCrafted, ItemCatalog.Furnace)]
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
        ((int)QuestEventKind.ItemBought).Should().Be(6);
        ((int)QuestEventKind.NodeBroken).Should().Be(7);
        ((int)QuestEventKind.ItemCrafted).Should().Be(8);
    }
}
