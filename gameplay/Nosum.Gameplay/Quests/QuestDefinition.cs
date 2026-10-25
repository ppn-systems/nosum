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
/// <c>quest.&lt;key&gt;</c>) and its objectives. A quest is complete when every objective is.
/// </summary>
public sealed class QuestDefinition
{
    private readonly QuestObjective[] _objectives;

    /// <summary>Initializes a quest.</summary>
    /// <param name="id">Stable numeric id, greater than zero.</param>
    /// <param name="key">Stable key.</param>
    /// <param name="objectives">The objectives, in display order.</param>
    public QuestDefinition(int id, string key, params QuestObjective[] objectives)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentOutOfRangeException.ThrowIfZero(objectives.Length);
        foreach (QuestObjective objective in objectives)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objective.Count);
        }

        this.Id = id;
        this.Key = key;
        _objectives = objectives;
    }

    /// <summary>Gets the stable numeric id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

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
