// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Quest;

namespace Nosum.Domain.Quests;

/// <summary>A player's progress on one quest. The key is (player, quest id from the quest catalogue).</summary>
public sealed class PlayerQuest
{
    /// <summary>Gets or sets the player.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the quest id from the quest catalogue.</summary>
    public int QuestId { get; set; }

    /// <summary>Gets or sets whether the quest is in progress or done.</summary>
    public QuestState State { get; set; } = QuestState.Active;

    /// <summary>Gets or sets how many each objective has counted, in the catalogue's objective order.</summary>
    public int[] Progress { get; set; } = [];

    /// <summary>Gets or sets when the player got the quest.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets when the quest was completed; null while active.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
