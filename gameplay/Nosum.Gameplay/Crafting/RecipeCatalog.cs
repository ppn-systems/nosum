// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Crafting;

/// <summary>
/// The recipes of the game (docs/adr/ADR-024 D2). Ids are append-only. The cooking recipes use the produce that exists now; swap their
/// inputs for potato and pumpkin when the crop roster (docs/adr/ADR-025) adds them - the output items and ids stay.
/// </summary>
public static class RecipeCatalog
{
    /// <summary>Chest.</summary>
    public const int Chest = 1;

    /// <summary>Wood fence.</summary>
    public const int WoodFence = 2;

    /// <summary>Stone path.</summary>
    public const int StonePath = 3;

    /// <summary>Scarecrow.</summary>
    public const int Scarecrow = 4;

    /// <summary>Sprinkler.</summary>
    public const int Sprinkler = 5;

    /// <summary>Furnace.</summary>
    public const int Furnace = 6;

    /// <summary>Salad.</summary>
    public const int Salad = 7;

    /// <summary>Baked potato.</summary>
    public const int BakedPotato = 8;

    /// <summary>Pumpkin soup.</summary>
    public const int PumpkinSoup = 9;

    /// <summary>Most items one craft request may make in a batch (so a request cannot ask for an absurd amount).</summary>
    public const int MaxBatch = 99;

    private static readonly RecipeDefinition[] s_all =
    [
        new(Chest, "chest", ItemCatalog.Chest, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Wood, 50)),
        new(WoodFence, "wood_fence", ItemCatalog.WoodFence, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Wood, 2)),
        new(StonePath, "stone_path", ItemCatalog.StonePath, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Stone, 1)),
        new(Scarecrow, "scarecrow", ItemCatalog.Scarecrow, 1, RecipeUnlock.Of(CraftSkill.Farming, 1), new RecipeInput(ItemCatalog.Wood, 50), new RecipeInput(ItemCatalog.Coal, 1), new RecipeInput(ItemCatalog.Fiber, 20)),
        new(Sprinkler, "sprinkler", ItemCatalog.Sprinkler, 1, RecipeUnlock.Of(CraftSkill.Farming, 2), new RecipeInput(ItemCatalog.CopperBar, 1), new RecipeInput(ItemCatalog.IronBar, 1)),
        new(Furnace, "furnace", ItemCatalog.Furnace, 1, RecipeUnlock.Of(CraftSkill.Mining, 1), new RecipeInput(ItemCatalog.CopperOre, 20), new RecipeInput(ItemCatalog.Stone, 25)),
        new(Salad, "salad", ItemCatalog.Salad, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Radish, 1), new RecipeInput(ItemCatalog.Cauliflower, 1)),
        new(BakedPotato, "baked_potato", ItemCatalog.BakedPotato, 1, RecipeUnlock.Of(CraftSkill.Farming, 1), new RecipeInput(ItemCatalog.Parsnip, 2), new RecipeInput(ItemCatalog.Wood, 1)),
        new(PumpkinSoup, "pumpkin_soup", ItemCatalog.PumpkinSoup, 1, RecipeUnlock.Of(CraftSkill.Farming, 3), new RecipeInput(ItemCatalog.Cauliflower, 2), new RecipeInput(ItemCatalog.Radish, 1), new RecipeInput(ItemCatalog.Wood, 1)),
    ];

    private static readonly Dictionary<int, RecipeDefinition> s_byId = Index();

    /// <summary>Gets every recipe, in id order.</summary>
    public static IReadOnlyList<RecipeDefinition> All => s_all;

    /// <summary>Looks up a recipe.</summary>
    /// <param name="recipeId">Recipe id.</param>
    /// <param name="recipe">The recipe when found.</param>
    /// <returns><see langword="true"/> when the id names a recipe.</returns>
    public static bool TryGet(int recipeId, [NotNullWhen(true)] out RecipeDefinition? recipe) => s_byId.TryGetValue(recipeId, out recipe);

    private static Dictionary<int, RecipeDefinition> Index()
    {
        Dictionary<int, RecipeDefinition> index = new(s_all.Length);
        foreach (RecipeDefinition recipe in s_all)
        {
            index.Add(recipe.Id, recipe);
        }

        return index;
    }
}
