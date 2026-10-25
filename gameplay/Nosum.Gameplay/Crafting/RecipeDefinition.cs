// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Gameplay.Crafting;

/// <summary>One kind of item a recipe consumes.</summary>
/// <param name="ItemId">Item id from the item catalogue.</param>
/// <param name="Quantity">How many are used by one craft.</param>
public readonly record struct RecipeInput(int ItemId, int Quantity);

/// <summary>What a recipe needs before it can be crafted: a skill level, or nothing (known from the start).</summary>
/// <param name="Skill">The skill, or <see cref="CraftSkill.None"/>.</param>
/// <param name="Level">The level of that skill, 1 to 10.</param>
public readonly record struct RecipeUnlock(CraftSkill Skill, int Level)
{
    /// <summary>Gets the unlock of a recipe every player knows from the start.</summary>
    public static RecipeUnlock Known => default;

    /// <summary>Gets whether the recipe is known from the start.</summary>
    public bool IsKnown => this.Skill == CraftSkill.None;

    /// <summary>Creates the unlock of a skill level.</summary>
    /// <param name="skill">The skill.</param>
    /// <param name="level">The level, 1 to 10.</param>
    /// <returns>The unlock.</returns>
    public static RecipeUnlock Of(CraftSkill skill, int level) => new(skill, level);
}

/// <summary>
/// A recipe as data: stable id (wire and tests), stable key (a recipe is named by its output item, so it has no text of its own),
/// what it makes, what it consumes and what unlocks it (docs/adr/ADR-024 D1). Crafting is one atomic transfer: inputs out, outputs in.
/// </summary>
public sealed class RecipeDefinition
{
    /// <summary>Initializes a recipe.</summary>
    /// <param name="id">Stable id, greater than zero; never reused.</param>
    /// <param name="key">Stable key.</param>
    /// <param name="outputItemId">The item made.</param>
    /// <param name="outputQuantity">How many of it one craft makes.</param>
    /// <param name="unlock">What unlocks the recipe.</param>
    /// <param name="inputs">What one craft consumes; at least one.</param>
    public RecipeDefinition(int id, string key, int outputItemId, int outputQuantity, RecipeUnlock unlock, params RecipeInput[] inputs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputQuantity);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Length == 0)
        {
            throw new ArgumentException("A recipe consumes something.", nameof(inputs));
        }

        this.Id = id;
        this.Key = key;
        this.OutputItemId = outputItemId;
        this.OutputQuantity = outputQuantity;
        this.Unlock = unlock;
        this.Inputs = inputs;
    }

    /// <summary>Gets the stable id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets the item one craft makes.</summary>
    public int OutputItemId { get; }

    /// <summary>Gets how many of the item one craft makes.</summary>
    public int OutputQuantity { get; }

    /// <summary>Gets what unlocks the recipe.</summary>
    public RecipeUnlock Unlock { get; }

    /// <summary>Gets what one craft consumes.</summary>
    public IReadOnlyList<RecipeInput> Inputs { get; }
}
