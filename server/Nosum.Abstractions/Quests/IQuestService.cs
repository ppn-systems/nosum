// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Quest;
using Nosum.Gameplay.Quests;

namespace Nosum.Abstractions.Quests;

/// <summary>
/// Tracks quest progress. Features report what the player did (<see cref="ReportAsync"/>); the
/// service counts it toward the player's active quests, pushes the new progress to the player and
/// raises <see cref="Completed"/> when a quest is finished, so rewards and unlocks hook in without
/// the quest knowing about them.
/// </summary>
public interface IQuestService
{
    /// <summary>Raised after a quest was completed and saved; handlers run one after another.</summary>
    event Func<QuestCompletion, ValueTask>? Completed;

    /// <summary>Lists the player's quests; a player who has none yet is given the starting ones.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The player's quests.</returns>
    Task<IReadOnlyList<QuestView>> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Counts something the player did toward every active quest it matches.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="questEvent">What happened.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when progress is saved, pushed and completion handlers ran.</returns>
    Task ReportAsync(Guid playerId, QuestEvent questEvent, CancellationToken cancellationToken = default);
}

/// <summary>One quest of a player.</summary>
/// <param name="QuestId">Quest id from the catalogue.</param>
/// <param name="State">Active or completed.</param>
/// <param name="Progress">Count per objective, in the catalogue's objective order.</param>
public sealed record QuestView(int QuestId, QuestState State, IReadOnlyList<int> Progress);

/// <summary>A quest the player just completed.</summary>
/// <param name="PlayerId">The player.</param>
/// <param name="QuestId">Quest id from the catalogue.</param>
public readonly record struct QuestCompletion(Guid PlayerId, int QuestId);
