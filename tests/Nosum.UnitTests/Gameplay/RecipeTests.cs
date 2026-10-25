// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The recipes and their unlock and affordability rules (docs/adr/ADR-024 D1, D2).</summary>
public sealed class RecipeTests
{
    private static int AllTen(CraftSkill skill) => 10;

    private static int Level(CraftSkill skill, int level) => skill == CraftSkill.None ? 0 : level;

    [Fact]
    public void Catalogue_IdsAreUnique_AndEveryItemExists()
    {
        RecipeCatalog.All.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        RecipeCatalog.All.Select(r => r.Key).Should().OnlyHaveUniqueItems();
        foreach (RecipeDefinition recipe in RecipeCatalog.All)
        {
            ItemCatalog.TryGet(recipe.OutputItemId, out _).Should().BeTrue($"{recipe.Key} makes a real item");
            recipe.Inputs.Should().OnlyContain(i => i.Quantity > 0, $"{recipe.Key} consumes something");
            foreach (RecipeInput input in recipe.Inputs)
            {
                ItemCatalog.TryGet(input.ItemId, out _).Should().BeTrue($"{recipe.Key} consumes real items");
            }

            recipe.Inputs.Select(i => i.ItemId).Should().OnlyHaveUniqueItems();
            recipe.Unlock.Level.Should().BeInRange(0, 10);
        }
    }

    [Fact]
    public void Catalogue_FirstRecipesMatchTheAdr()
    {
        Cost(RecipeCatalog.Chest).Should().BeEquivalentTo([(ItemCatalog.Wood, 50)]);
        Cost(RecipeCatalog.WoodFence).Should().BeEquivalentTo([(ItemCatalog.Wood, 2)]);
        Cost(RecipeCatalog.StonePath).Should().BeEquivalentTo([(ItemCatalog.Stone, 1)]);
        Cost(RecipeCatalog.Scarecrow).Should().BeEquivalentTo([(ItemCatalog.Wood, 50), (ItemCatalog.Coal, 1), (ItemCatalog.Fiber, 20)]);
        Cost(RecipeCatalog.Sprinkler).Should().BeEquivalentTo([(ItemCatalog.CopperBar, 1), (ItemCatalog.IronBar, 1)]);
        Cost(RecipeCatalog.Furnace).Should().BeEquivalentTo([(ItemCatalog.CopperOre, 20), (ItemCatalog.Stone, 25)]);
        Unlock(RecipeCatalog.Chest).IsKnown.Should().BeTrue();
        Unlock(RecipeCatalog.Scarecrow).Should().Be(RecipeUnlock.Of(CraftSkill.Farming, 1));
        Unlock(RecipeCatalog.Sprinkler).Should().Be(RecipeUnlock.Of(CraftSkill.Farming, 2));
        Unlock(RecipeCatalog.Furnace).Should().Be(RecipeUnlock.Of(CraftSkill.Mining, 1));
    }

    [Fact]
    public void Catalogue_CraftedPlaceablesAreItemsThatCanBePlaced_AndCookingMakesFood()
    {
        int[] placeables = [RecipeCatalog.Chest, RecipeCatalog.WoodFence, RecipeCatalog.StonePath, RecipeCatalog.Scarecrow, RecipeCatalog.Sprinkler, RecipeCatalog.Furnace];
        foreach (int id in placeables)
        {
            _ = RecipeCatalog.TryGet(id, out RecipeDefinition? recipe).Should().BeTrue();
            PlaceableCatalog.TryGet(recipe!.OutputItemId, out _).Should().BeTrue($"{recipe.Key} is placeable");
        }

        int[] dishes = [RecipeCatalog.Salad, RecipeCatalog.BakedPotato, RecipeCatalog.PumpkinSoup];
        foreach (int id in dishes)
        {
            _ = RecipeCatalog.TryGet(id, out RecipeDefinition? recipe).Should().BeTrue();
            ItemCatalog.TryGet(recipe!.OutputItemId, out ItemDefinition? output).Should().BeTrue();
            output!.Kind.Should().Be(ItemKind.Food);
            output.Id.Should().BeInRange(700, 799);
        }
    }

    [Fact]
    public void Items_FollowTheIdBands()
    {
        foreach (ItemDefinition item in ItemCatalog.All.Where(i => i.Kind == ItemKind.Placeable))
        {
            item.Id.Should().BeInRange(600, 699);
        }

        foreach (ItemDefinition item in ItemCatalog.All.Where(i => i.Kind == ItemKind.Food))
        {
            item.Id.Should().BeInRange(700, 799);
        }

        ItemCatalog.All.Where(i => i.Kind == ItemKind.Resource).Should().OnlyContain(i => i.Id >= 500 && i.Id <= 599);
    }

    [Fact]
    public void Check_KnownRecipe_NeedsNoSkill()
    {
        RecipeRules.Check(RecipeCatalog.Chest, 1, _ => 0).Should().Be(CraftVerdict.Ok);
    }

    [Theory]
    [InlineData(0, CraftVerdict.Locked)]
    [InlineData(1, CraftVerdict.Locked)]
    [InlineData(2, CraftVerdict.Ok)]
    [InlineData(10, CraftVerdict.Ok)]
    public void Check_SprinklerNeedsFarmingTwo(int farming, CraftVerdict expected)
    {
        RecipeRules.Check(RecipeCatalog.Sprinkler, 1, skill => Level(skill, skill == CraftSkill.Farming ? farming : 0)).Should().Be(expected);
    }

    [Fact]
    public void Check_AnotherSkillDoesNotUnlock()
    {
        RecipeRules.Check(RecipeCatalog.Furnace, 1, skill => skill == CraftSkill.Mining ? 0 : 10).Should().Be(CraftVerdict.Locked);
        RecipeRules.Check(RecipeCatalog.Furnace, 1, skill => skill == CraftSkill.Mining ? 1 : 0).Should().Be(CraftVerdict.Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Check_BatchOutsideOneToNinetyNine_IsInvalid(int times)
    {
        RecipeRules.Check(RecipeCatalog.WoodFence, times, AllTen).Should().Be(CraftVerdict.InvalidQuantity);
    }

    [Fact]
    public void Check_UnknownRecipe_IsRefused()
    {
        RecipeRules.Check(9999, 1, AllTen).Should().Be(CraftVerdict.UnknownRecipe);
        RecipeRules.Check(0, 1, AllTen).Should().Be(CraftVerdict.UnknownRecipe);
    }

    [Fact]
    public void CanCraft_WithTheInputs_IsOk_AndDoesNotChangeTheGrid()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(ItemCatalog.Wood, 60);
        _ = RecipeCatalog.TryGet(RecipeCatalog.Chest, out RecipeDefinition? chest);

        RecipeRules.CanCraft(chest!, 1, grid).Should().Be(InventoryVerdict.Ok);

        grid.CountOf(ItemCatalog.Wood).Should().Be(60);
        grid.CountOf(ItemCatalog.Chest).Should().Be(0);
    }

    [Fact]
    public void CanCraft_WithTooFewInputs_IsNotEnough()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(ItemCatalog.Wood, 49);
        _ = RecipeCatalog.TryGet(RecipeCatalog.Chest, out RecipeDefinition? chest);

        RecipeRules.CanCraft(chest!, 1, grid).Should().Be(InventoryVerdict.NotEnough);
        RecipeRules.CanCraft(chest!, 2, grid).Should().Be(InventoryVerdict.NotEnough);
    }

    [Fact]
    public void CanCraft_FullInventory_IsNoSpace_UnlessTheInputsFreeASlot()
    {
        // One slot holding exactly the 50 wood: crafting frees it, so the chest fits.
        InventoryGrid exact = new(1);
        _ = exact.Add(ItemCatalog.Wood, 50);
        // One slot holding 99 wood of 2 needed per fence: the stack is only partly used, no room for the fence stack beside it.
        InventoryGrid partly = new(1);
        _ = partly.Add(ItemCatalog.Wood, 99);
        _ = RecipeCatalog.TryGet(RecipeCatalog.Chest, out RecipeDefinition? chest);

        RecipeRules.CanCraft(chest!, 1, exact).Should().Be(InventoryVerdict.Ok);
        RecipeRules.CanCraft(chest!, 1, partly).Should().Be(InventoryVerdict.NoSpace);
    }

    [Fact]
    public void InputOf_ScalesWithTheBatch()
    {
        _ = RecipeCatalog.TryGet(RecipeCatalog.Scarecrow, out RecipeDefinition? scarecrow);

        RecipeRules.InputOf(scarecrow!, ItemCatalog.Fiber, 3).Should().Be(60);
        RecipeRules.InputOf(scarecrow!, ItemCatalog.Stone, 3).Should().Be(0);
    }

    private static (int ItemId, int Quantity)[] Cost(int recipeId)
    {
        _ = RecipeCatalog.TryGet(recipeId, out RecipeDefinition? recipe).Should().BeTrue();
        return [.. recipe!.Inputs.Select(i => (i.ItemId, i.Quantity))];
    }

    private static RecipeUnlock Unlock(int recipeId)
    {
        _ = RecipeCatalog.TryGet(recipeId, out RecipeDefinition? recipe).Should().BeTrue();
        return recipe!.Unlock;
    }
}
