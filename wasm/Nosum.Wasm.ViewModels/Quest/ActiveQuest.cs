// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Contracts.Quest;
using Nosum.Gameplay.Quests;
using Nosum.Wasm.Socket.Quest;

namespace Nosum.Wasm.ViewModels.Quest;

/// <summary>
/// The quest the farmer is working on now: the first active quest in the tutorial chain's order (docs/adr/ADR-036 D1), the same one the tracker shows.
/// The waypoint and the merchants' greeting both read it, so they agree.
/// </summary>
public static class ActiveQuest
{
    /// <summary>Finds the active quest.</summary>
    /// <param name="quests">The farmer's quests, as the quest mirror holds them.</param>
    /// <returns>The definition of the first active quest in chain order, or <see langword="null"/> when none is active.</returns>
    public static QuestDefinition? Of(IReadOnlyList<QuestInfo> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        foreach (int questId in QuestCatalog.Chain)
        {
            foreach (QuestInfo quest in quests)
            {
                if (quest.QuestId == questId && quest.State == QuestState.Active && QuestCatalog.TryGet(questId, out QuestDefinition? definition))
                {
                    return definition;
                }
            }
        }

        return null;
    }
}
