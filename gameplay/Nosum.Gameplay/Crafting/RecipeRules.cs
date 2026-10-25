// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Inventory;

namespace Nosum.Gameplay.Crafting;

/// <summary>Why a craft is allowed or not. The server maps it to a wire status.</summary>
public enum CraftVerdict
{
    /// <summary>The craft may go ahead.</summary>
    Ok = 0,

    /// <summary>No recipe has that id.</summary>
    UnknownRecipe = 1,

    /// <summary>The batch size is zero, negative or above <see cref="RecipeCatalog.MaxBatch"/>.</summary>
    InvalidQuantity = 2,

    /// <summary>The player's skill level does not unlock the recipe yet.</summary>
    Locked = 3,
}

/// <summary>The pure rules of crafting (docs/adr/ADR-024 D1): what unlocks a recipe and what a batch costs and makes.</summary>
public static class RecipeRules
{
    /// <summary>Checks that a recipe may be crafted by a player with a skill level.</summary>
    /// <param name="recipeId">Recipe id.</param>
    /// <param name="times">How many crafts.</param>
    /// <param name="levelOf">The player's level in a skill (0 to 10); not asked for a recipe known from the start.</param>
    /// <returns>Ok, or the first reason it may not.</returns>
    public static CraftVerdict Check(int recipeId, int times, Func<CraftSkill, int> levelOf)
    {
        ArgumentNullException.ThrowIfNull(levelOf);

        if (!RecipeCatalog.TryGet(recipeId, out RecipeDefinition? recipe))
        {
            return CraftVerdict.UnknownRecipe;
        }

        if (times is < 1 or > RecipeCatalog.MaxBatch)
        {
            return CraftVerdict.InvalidQuantity;
        }

        return IsUnlocked(recipe, levelOf) ? CraftVerdict.Ok : CraftVerdict.Locked;
    }

    /// <summary>Tells whether a recipe is unlocked for a player.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="levelOf">The player's level in a skill.</param>
    /// <returns><see langword="true"/> when it is known from the start or the skill level is high enough.</returns>
    public static bool IsUnlocked(RecipeDefinition recipe, Func<CraftSkill, int> levelOf)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(levelOf);

        return recipe.Unlock.IsKnown || levelOf(recipe.Unlock.Skill) >= recipe.Unlock.Level;
    }

    /// <summary>
    /// Tells whether the items fit: after removing what <paramref name="times"/> crafts consume, a copy of the grid must still have room for what they make.
    /// Used by the client to grey a button; the server's transfer decides.
    /// </summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="times">How many crafts.</param>
    /// <param name="inventory">The player's inventory (not changed).</param>
    /// <returns><see cref="InventoryVerdict.Ok"/>, <see cref="InventoryVerdict.NotEnough"/> or <see cref="InventoryVerdict.NoSpace"/>.</returns>
    public static InventoryVerdict CanCraft(RecipeDefinition recipe, int times, InventoryGrid inventory)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(inventory);

        InventoryGrid copy = new(inventory.Capacity);
        for (int i = 0; i < inventory.Capacity; i++)
        {
            if (!inventory[i].IsEmpty)
            {
                copy.Load(i, inventory[i]);
            }
        }

        foreach (RecipeInput input in recipe.Inputs)
        {
            InventoryVerdict removed = copy.Remove(input.ItemId, checked(input.Quantity * times));
            if (removed != InventoryVerdict.Ok)
            {
                return removed;
            }
        }

        return copy.Add(recipe.OutputItemId, checked(recipe.OutputQuantity * times));
    }

    /// <summary>Tells how many of an item one batch uses.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="times">How many crafts.</param>
    /// <returns>The quantity, zero when the recipe does not use the item.</returns>
    public static int InputOf(RecipeDefinition recipe, int itemId, int times)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        foreach (RecipeInput input in recipe.Inputs)
        {
            if (input.ItemId == itemId)
            {
                return input.Quantity * times;
            }
        }

        return 0;
    }
}
