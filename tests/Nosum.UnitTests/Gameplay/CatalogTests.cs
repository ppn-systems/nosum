// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Quests;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class CatalogTests
{
    [Fact]
    public void ItemCatalog_Items_HaveUniqueIdsAndKeys()
    {
        HashSet<int> ids = [];
        HashSet<string> keys = [];
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            ids.Add(item.Id).Should().BeTrue($"item id {item.Id} is unique");
            keys.Add(item.Key).Should().BeTrue($"item key {item.Key} is unique");
            item.MaxStack.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void ItemCatalog_Seeds_GrowAKnownCropThatYieldsAKnownItem()
    {
        foreach (ItemDefinition seed in ItemCatalog.All)
        {
            if (seed.Kind != ItemKind.Seed)
            {
                continue;
            }

            CropCatalog.TryGet(seed.CropId, out CropDefinition? crop).Should().BeTrue();
            ItemCatalog.TryGet(crop!.HarvestItemId, out ItemDefinition? produce).Should().BeTrue();
            produce!.Kind.Should().Be(ItemKind.Produce);
        }
    }

    [Fact]
    public void ItemCatalog_Tools_NameTheirToolKind()
    {
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            (item.Kind == ItemKind.Tool).Should().Be(item.Tool != ToolKind.None);
        }
    }

    [Fact]
    public void CropCatalog_Crops_HaveUniqueIdsAndGrowthStages()
    {
        HashSet<int> ids = [];
        foreach (CropDefinition crop in CropCatalog.All)
        {
            ids.Add(crop.Id).Should().BeTrue();
            crop.StageCount.Should().BeGreaterThan(1);
            crop.TotalGrowthDays.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void QuestCatalog_FirstHarvest_NeedsOnePlantingAndOneHarvest()
    {
        QuestCatalog.TryGet(QuestCatalog.FirstHarvest, out QuestDefinition? quest).Should().BeTrue();
        int[] progress = new int[quest!.ObjectiveCount];

        quest.Apply(progress, new QuestEvent(QuestEventKind.CropPlanted, CropCatalog.Radish)).Should().BeTrue();
        quest.IsComplete(progress).Should().BeFalse();
        quest.Apply(progress, new QuestEvent(QuestEventKind.CropHarvested, CropCatalog.Radish)).Should().BeTrue();

        quest.IsComplete(progress).Should().BeTrue();
    }

    [Fact]
    public void QuestDefinition_Apply_CapsAtTheTargetAndIgnoresOtherEvents()
    {
        QuestDefinition quest = new(99, "test", new QuestObjective(QuestEventKind.CropPlanted, CropCatalog.Parsnip, 2));
        int[] progress = [0];

        quest.Apply(progress, new QuestEvent(QuestEventKind.CropPlanted, CropCatalog.Radish)).Should().BeFalse();
        quest.Apply(progress, new QuestEvent(QuestEventKind.CropHarvested, CropCatalog.Parsnip)).Should().BeFalse();
        quest.Apply(progress, new QuestEvent(QuestEventKind.CropPlanted, CropCatalog.Parsnip, 5)).Should().BeTrue();
        quest.Apply(progress, new QuestEvent(QuestEventKind.CropPlanted, CropCatalog.Parsnip)).Should().BeFalse();

        progress[0].Should().Be(2);
        quest.IsComplete(progress).Should().BeTrue();
    }
}
