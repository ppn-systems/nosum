// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Crafting;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Skills;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Crafting;

namespace Nosum.Infrastructure.Crafting;

/// <summary>
/// <see cref="ICraftingService"/>: checks the unlock with <see cref="RecipeRules"/>, then one <see cref="IInventoryService.ApplyAsync"/> removes the inputs and adds the
/// outputs, so a craft is all or nothing, serialized with every other change of the player's items and audited with the reason <c>craft.&lt;key&gt;</c>
/// (docs/adr/ADR-024 D1). Removals come first, so room freed by the inputs counts for the output.
/// </summary>
public sealed class CraftingService : ICraftingService
{
    private readonly IMapPresence _presence;
    private readonly IInventoryService _inventory;
    private readonly ISkillLevels _skills;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who is signed in on which connection; the account id comes from it, never from the client.</param>
    /// <param name="inventory">Moves the items.</param>
    /// <param name="skills">The players' skill levels.</param>
    public CraftingService(IMapPresence presence, IInventoryService inventory, ISkillLevels skills)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(skills);

        _presence = presence;
        _inventory = inventory;
        _skills = skills;
    }

    /// <inheritdoc/>
    public async Task<CraftOutcome> CraftAsync(IConnection connection, int recipeId, int times, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new CraftOutcome(CraftStatus.UnknownRecipe, 0);
        }

        if (!RecipeCatalog.TryGet(recipeId, out RecipeDefinition? recipe))
        {
            return new CraftOutcome(CraftStatus.UnknownRecipe, 0);
        }

        // Only a recipe with an unlock asks for a level; the skills are read once per skill the recipe needs.
        Dictionary<CraftSkill, int> levels = [];
        if (!recipe.Unlock.IsKnown)
        {
            levels[recipe.Unlock.Skill] = await _skills.LevelOfAsync(player.AccountId, recipe.Unlock.Skill, cancellationToken).ConfigureAwait(false);
        }

        CraftVerdict verdict = RecipeRules.Check(recipeId, times, skill => levels.GetValueOrDefault(skill));
        if (verdict != CraftVerdict.Ok)
        {
            return new CraftOutcome(ToStatus(verdict), 0);
        }

        List<InventoryOperation> operations = new(recipe.Inputs.Count + 1);
        foreach (RecipeInput input in recipe.Inputs)
        {
            operations.Add(InventoryOperation.Remove(input.ItemId, input.Quantity * times));
        }

        operations.Add(InventoryOperation.Add(recipe.OutputItemId, recipe.OutputQuantity * times));
        InventoryChange change = await _inventory.ApplyAsync(player.AccountId, operations, "craft." + recipe.Key, null, cancellationToken).ConfigureAwait(false);
        return change.IsOk ? new CraftOutcome(CraftStatus.Ok, times) : new CraftOutcome(ToStatus(change.Status), 0);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<int>> UnlockedAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return [];
        }

        Dictionary<CraftSkill, int> levels = [];
        List<int> unlocked = [];
        foreach (RecipeDefinition recipe in RecipeCatalog.All)
        {
            if (!recipe.Unlock.IsKnown && !levels.ContainsKey(recipe.Unlock.Skill))
            {
                levels[recipe.Unlock.Skill] = await _skills.LevelOfAsync(player.AccountId, recipe.Unlock.Skill, cancellationToken).ConfigureAwait(false);
            }

            if (RecipeRules.IsUnlocked(recipe, skill => levels.GetValueOrDefault(skill)))
            {
                unlocked.Add(recipe.Id);
            }
        }

        return unlocked;
    }

    private static CraftStatus ToStatus(CraftVerdict verdict) => verdict switch
    {
        CraftVerdict.Ok => CraftStatus.Ok,
        CraftVerdict.UnknownRecipe => CraftStatus.UnknownRecipe,
        CraftVerdict.InvalidQuantity => CraftStatus.InvalidQuantity,
        _ => CraftStatus.Locked,
    };

    private static CraftStatus ToStatus(InventoryStatus status) => status switch
    {
        InventoryStatus.NotEnough => CraftStatus.NotEnough,
        InventoryStatus.NoSpace => CraftStatus.NoSpace,
        _ => CraftStatus.Conflict,
    };
}
