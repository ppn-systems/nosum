// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Quest;

/// <summary>Where a quest stands for a player. Values are append-only.</summary>
public enum QuestState : byte
{
    /// <summary>The quest is in progress.</summary>
    Active = 1,

    /// <summary>Every objective is done.</summary>
    Completed = 2,
}
