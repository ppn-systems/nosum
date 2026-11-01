// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Quests;

/// <summary>What a player did that a quest objective may count. Values are append-only (they are stored in nothing, but keep them stable).</summary>
public enum QuestEventKind
{
    /// <summary>A crop was planted; the subject is the crop id.</summary>
    CropPlanted = 1,

    /// <summary>A crop was harvested; the subject is the crop id.</summary>
    CropHarvested = 2,

    /// <summary>Soil was tilled; no subject.</summary>
    SoilTilled = 3,

    /// <summary>Soil was watered; no subject.</summary>
    CropWatered = 4,

    /// <summary>Items were put into the shipping bin; the subject is the item id, the amount how many.</summary>
    ItemShipped = 5,

    /// <summary>Seeds (of a crop or a tree) were bought at a shop; the subject is the item id, the amount how many.</summary>
    SeedBought = 6,

    /// <summary>A resource node broke (a tree fell, a stone was gone); the subject is the <c>NodeKind</c>.</summary>
    NodeBroken = 7,

    /// <summary>Something was crafted; the subject is the item id of the result, the amount how many.</summary>
    ItemCrafted = 8,

    /// <summary>A fish was reeled in from the water; the subject is the fish's item id.</summary>
    FishCaught = 9,

    /// <summary>A monster was defeated by the player; the subject is the monster kind.</summary>
    MonsterDefeated = 10,

    /// <summary>A tool was upgraded at the blacksmith and collected; the subject is the upgraded tool's item id.</summary>
    ToolUpgraded = 11,

    /// <summary>A building was placed on the farm; the subject is the blueprint item id it was built from.</summary>
    BuildingPlaced = 12,

    /// <summary>An animal was bought for a building; the subject is the animal kind.</summary>
    AnimalBought = 13,

    /// <summary>Food was eaten; the subject is the food item id.</summary>
    FoodEaten = 14,

    /// <summary>A request on the Town board was filled; the subject is the requested item id.</summary>
    RequestFilled = 15,

    /// <summary>A placeable (furniture or a crafted placeable) was set down; the subject is the item id.</summary>
    PlaceablePlaced = 16,

    /// <summary>A pet was adopted; the subject is the pet kind.</summary>
    PetAdopted = 17,

    /// <summary>An emote was sent in the world; the subject is the emote id.</summary>
    EmoteSent = 18,
}

/// <summary>One thing a player did, reported by a feature to the quest service.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="SubjectId">What it happened to (a crop id, an item id); zero when it has no subject.</param>
/// <param name="Amount">How many times; at least one.</param>
public readonly record struct QuestEvent(QuestEventKind Kind, int SubjectId = 0, int Amount = 1);

/// <summary>One step of a quest: do <see cref="Count"/> of something.</summary>
/// <param name="Kind">Which events count.</param>
/// <param name="SubjectId">Only events about this subject count; zero counts every subject.</param>
/// <param name="Count">How many events complete the objective.</param>
public readonly record struct QuestObjective(QuestEventKind Kind, int SubjectId, int Count)
{
    /// <summary>Returns whether an event counts toward this objective.</summary>
    /// <param name="e">The event.</param>
    /// <returns><see langword="true"/> when kind and subject match.</returns>
    public bool Matches(in QuestEvent e) => e.Kind == this.Kind && (this.SubjectId == 0 || this.SubjectId == e.SubjectId);
}

/// <summary>
/// A quest as data: a stable id and key (texts live in the localization catalogue under
/// <c>quest.&lt;key&gt;</c>), the coins completing it pays (docs/adr/ADR-036 D2) and its objectives. A quest is complete when every objective is.
/// </summary>
public sealed class QuestDefinition
{
    private readonly QuestObjective[] _objectives;

    /// <summary>Initializes a quest.</summary>
    /// <param name="id">Stable numeric id, greater than zero.</param>
    /// <param name="key">Stable key.</param>
    /// <param name="rewardCoins">Coins paid once when the quest is completed; zero for none.</param>
    /// <param name="objectives">The objectives, in display order.</param>
    public QuestDefinition(int id, string key, long rewardCoins, params QuestObjective[] objectives)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfNegative(rewardCoins);
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentOutOfRangeException.ThrowIfZero(objectives.Length);
        foreach (QuestObjective objective in objectives)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objective.Count);
        }

        this.Id = id;
        this.Key = key;
        this.RewardCoins = rewardCoins;
        _objectives = objectives;
    }

    /// <summary>Gets the stable numeric id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets the coins paid once when the quest is completed; zero when it pays nothing.</summary>
    public long RewardCoins { get; }

    /// <summary>Gets the number of objectives.</summary>
    public int ObjectiveCount => _objectives.Length;

    /// <summary>Gets an objective.</summary>
    /// <param name="index">Objective number, zero based.</param>
    /// <returns>The objective.</returns>
    public QuestObjective ObjectiveAt(int index) => _objectives[index];

    /// <summary>
    /// Counts an event toward the objectives. Each objective stops counting at its target.
    /// </summary>
    /// <param name="progress">Count per objective, updated in place; its length must equal <see cref="ObjectiveCount"/>.</param>
    /// <param name="e">The event.</param>
    /// <returns><see langword="true"/> when any count changed.</returns>
    public bool Apply(int[] progress, in QuestEvent e)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentOutOfRangeException.ThrowIfNotEqual(progress.Length, _objectives.Length);

        bool changed = false;
        for (int i = 0; i < _objectives.Length; i++)
        {
            QuestObjective objective = _objectives[i];
            if (objective.Matches(e) && progress[i] < objective.Count)
            {
                progress[i] = Math.Min(objective.Count, progress[i] + Math.Max(1, e.Amount));
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Returns whether every objective reached its target.</summary>
    /// <param name="progress">Count per objective.</param>
    /// <returns><see langword="true"/> when the quest is complete.</returns>
    public bool IsComplete(int[] progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentOutOfRangeException.ThrowIfNotEqual(progress.Length, _objectives.Length);

        for (int i = 0; i < _objectives.Length; i++)
        {
            if (progress[i] < _objectives[i].Count)
            {
                return false;
            }
        }

        return true;
    }
}
