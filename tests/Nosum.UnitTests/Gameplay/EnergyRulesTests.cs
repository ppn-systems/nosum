// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The daily energy budget (docs/adr/ADR-023 D1, D2).</summary>
public sealed class EnergyRulesTests
{
    [Theory]
    [InlineData(ToolKind.Hoe, 3)]
    [InlineData(ToolKind.WateringCan, 3)]
    [InlineData(ToolKind.Axe, 3)]
    [InlineData(ToolKind.Pickaxe, 3)]
    [InlineData(ToolKind.Scythe, 0)]
    [InlineData(ToolKind.None, 0)]
    public void BaseCost_PerTool_MatchesTheAdr(ToolKind tool, int expected) => EnergyRules.BaseCost(tool).Should().Be(expected);

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    [InlineData(7, 2)]
    [InlineData(8, 1)]
    [InlineData(10, 1)]
    public void Cost_EveryFourLevelsTakesOffOne_ButNeverBelowOne(int level, int expected) => EnergyRules.Cost(ToolKind.Hoe, level).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Cost_Scythe_StaysFree(int level) => EnergyRules.Cost(ToolKind.Scythe, level).Should().Be(0);

    [Fact]
    public void Refill_WhenADayPassed_FillsAndMarksToday()
    {
        EnergyRules.Refill(3, 4, 5).Should().Be((EnergyRules.MaxEnergy, 5L));
        EnergyRules.Refill(0, 0, 400).Should().Be((EnergyRules.MaxEnergy, 400L));
    }

    [Fact]
    public void Refill_WithinTheSameDay_KeepsTheEnergy() => EnergyRules.Refill(3, 5, 5).Should().Be((3, 5L));

    [Fact]
    public void Refill_ANewPlayer_StartsFull() => EnergyRules.Refill(0, EnergyRules.NeverRefilled, 0).Should().Be((EnergyRules.MaxEnergy, 0L));

    [Fact]
    public void Refill_WhenTheClockWentBack_KeepsTheEnergy() => EnergyRules.Refill(7, 9, 8).Should().Be((7, 9L));

    [Fact]
    public void TrySpend_WithEnough_Spends()
    {
        EnergyRules.TrySpend(10, 2, out int left).Should().BeTrue();
        left.Should().Be(8);
    }

    [Fact]
    public void TrySpend_AtZero_IsRefusedAndKeepsTheEnergy()
    {
        EnergyRules.TrySpend(0, 2, out int left).Should().BeFalse();
        left.Should().Be(0);
    }

    [Fact]
    public void TrySpend_OneShortOfTheCost_IsRefused() => EnergyRules.TrySpend(1, 2, out _).Should().BeFalse();

    [Fact]
    public void TrySpend_AFreeUse_WorksAtZero()
    {
        EnergyRules.TrySpend(0, 0, out int left).Should().BeTrue();
        left.Should().Be(0);
    }

    [Fact]
    public void Eat_CapsAtTheMaximum()
    {
        EnergyRules.Eat(100, 20).Should().Be(120);
        EnergyRules.Eat(EnergyRules.MaxEnergy - 3, 20).Should().Be(EnergyRules.MaxEnergy);
    }

    [Fact]
    public void RawCrops_AreEdible_AndTheirValuesAreSmall()
    {
        foreach (int id in new[] { ItemCatalog.Parsnip, ItemCatalog.Radish, ItemCatalog.Cauliflower })
        {
            ItemCatalog.TryGet(id, out ItemDefinition? crop).Should().BeTrue();
            crop!.IsEdible.Should().BeTrue();
            crop.FoodEnergy.Should().BeInRange(1, 30);
        }
    }

    [Fact]
    public void Tools_Seeds_AndResources_AreNotEdible()
    {
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            if (item.Kind is ItemKind.Tool or ItemKind.Seed or ItemKind.Resource or ItemKind.Furniture or ItemKind.Clothing)
            {
                item.IsEdible.Should().BeFalse($"{item.Key} is not food");
            }
        }
    }
}
