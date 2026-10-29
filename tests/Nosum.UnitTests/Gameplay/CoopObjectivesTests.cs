// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The pure rules of shared co-op objectives (docs/adr/ADR-067): the catalogue, the ledger and when the trophy is earned.</summary>
public sealed class CoopObjectivesTests
{
    [Fact]
    public void Catalog_EveryObjective_HasAUniqueIdKeyAndPositiveTarget()
    {
        CoopObjectiveCatalog.All.Select(o => o.Id).Should().OnlyHaveUniqueItems();
        CoopObjectiveCatalog.All.Select(o => o.Key).Should().OnlyHaveUniqueItems();
        CoopObjectiveCatalog.All.Should().OnlyContain(o => o.Id > 0 && o.Target > 0 && !string.IsNullOrEmpty(o.Key));
    }

    [Fact]
    public void Catalog_TryForItem_ACroppedItemWithAnObjective_IsFound()
    {
        CoopObjectiveCatalog.TryForItem(ItemCatalog.Strawberry, out CoopObjectiveDefinition? objective).Should().BeTrue();

        objective!.ItemId.Should().Be(ItemCatalog.Strawberry);
        objective.Id.Should().Be(CoopObjectiveCatalog.StrawberryOrchard);
    }

    [Fact]
    public void Catalog_TryForItem_AnItemNoObjectiveCounts_IsNotFound()
    {
        CoopObjectiveCatalog.TryForItem(ItemCatalog.Wood, out CoopObjectiveDefinition? objective).Should().BeFalse();

        objective.Should().BeNull();
    }

    [Fact]
    public void Catalog_TryGet_AnUnknownId_IsNotFound()
    {
        CoopObjectiveCatalog.TryGet(0, out CoopObjectiveDefinition? objective).Should().BeFalse();
        CoopObjectiveCatalog.TryGet(int.MaxValue, out _).Should().BeFalse();
        objective.Should().BeNull();
    }

    [Fact]
    public void Credit_AddsTheHarvest_ToTheTotal()
    {
        CoopObjectiveRules.Credit(120, 15).Should().Be(135);
    }

    [Fact]
    public void Credit_AtTheLimit_SaturatesInsteadOfWrapping()
    {
        CoopObjectiveRules.Credit(int.MaxValue - 2, 10).Should().Be(int.MaxValue);
    }

    [Theory]
    [InlineData(299, 300, false)]
    [InlineData(300, 300, true)]
    [InlineData(301, 300, true)]
    public void IsComplete_ReachingTheTarget_IsComplete(int contributed, int target, bool expected)
    {
        CoopObjectiveRules.IsComplete(contributed, target).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 100, 300, false, false)]
    [InlineData(250, 300, 300, false, true)]
    [InlineData(300, 320, 300, false, false)]
    [InlineData(250, 300, 300, true, false)]
    public void EarnsTrophy_OnlyTheHarvestThatReachesTheTarget_EarnsItOnce(int before, int after, int target, bool alreadyEarned, bool expected)
    {
        CoopObjectiveRules.EarnsTrophy(before, after, target, alreadyEarned).Should().Be(expected);
    }
}
