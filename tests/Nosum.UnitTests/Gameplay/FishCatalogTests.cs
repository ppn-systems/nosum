// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Skills;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The fish species, the rod and the fishing recipes (docs/adr/ADR-078 D2, D3).</summary>
public sealed class FishCatalogTests
{
    [Fact]
    public void All_HasTenUniqueSpecies() => FishCatalog.All.Select(species => species.ItemId).Should().OnlyHaveUniqueItems().And.HaveCount(10);

    [Fact]
    public void All_EverySpecies_IsAFishItemWithAPrice()
    {
        foreach (FishSpecies species in FishCatalog.All)
        {
            ItemCatalog.TryGet(species.ItemId, out ItemDefinition? item).Should().BeTrue(species.Key);
            item!.Kind.Should().Be(ItemKind.Produce, species.Key);
            item.SellPrice.Should().BeGreaterThan(0, species.Key);
        }
    }

    [Fact]
    public void All_EverySpecies_IsCaughtSomewhere()
    {
        foreach (FishSpecies species in FishCatalog.All)
        {
            species.Seasons.Should().NotBe(Nosum.Gameplay.Calendar.SeasonSet.None, species.Key);
            species.Water.Should().NotBe(FishingWater.None, species.Key);
        }
    }

    [Fact]
    public void TryGet_FishItem_ReturnsItsSpecies()
    {
        FishCatalog.TryGet(ItemCatalog.GoldenKoi, out FishSpecies? koi).Should().BeTrue();

        koi!.Rarity.Should().Be(FishRarity.Legendary);
    }

    [Fact]
    public void TryGet_NonFish_IsFalse() => FishCatalog.TryGet(ItemCatalog.Hoe, out _).Should().BeFalse();

    [Fact]
    public void FishingRod_IsAToolThatTrainsFishing()
    {
        ItemCatalog.TryGet(ItemCatalog.FishingRod, out ItemDefinition? rod).Should().BeTrue();

        rod!.Kind.Should().Be(ItemKind.Tool);
        rod.Tool.Should().Be(ToolKind.FishingRod);
        SkillRules.SkillOf(ToolKind.FishingRod).Should().Be(SkillKind.Fishing);
        rod.IsTradable.Should().BeFalse();
    }

    [Fact]
    public void FishingRod_HasARecipe()
    {
        RecipeCatalog.TryGet(RecipeCatalog.FishingRod, out RecipeDefinition? recipe).Should().BeTrue();

        recipe!.OutputItemId.Should().Be(ItemCatalog.FishingRod);
    }

    [Fact]
    public void FishStew_IsCookedFromFish()
    {
        RecipeCatalog.TryGet(RecipeCatalog.FishStew, out RecipeDefinition? recipe).Should().BeTrue();

        recipe!.OutputItemId.Should().Be(ItemCatalog.FishStew);
        recipe.Inputs.Should().Contain(input => input.ItemId == ItemCatalog.Anchovy);
        ItemCatalog.TryGet(ItemCatalog.FishStew, out ItemDefinition? stew).Should().BeTrue();
        stew!.IsEdible.Should().BeTrue();
    }

    [Theory]
    [InlineData(GameMaps.Beach, FishingWater.Beach)]
    [InlineData(GameMaps.Woods, FishingWater.Woods)]
    [InlineData(GameMaps.Farm, FishingWater.Farm)]
    [InlineData(GameMaps.Town, FishingWater.None)]
    public void Of_Map_GivesItsFishingWater(string mapId, FishingWater expected) => FishingWaters.Of(mapId).Should().Be(expected);
}
