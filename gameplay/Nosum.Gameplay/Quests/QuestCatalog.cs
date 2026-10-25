// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Nosum.Gameplay.Quests;

/// <summary>Every quest the game knows. Ids are append-only.</summary>
public static class QuestCatalog
{
    /// <summary>The tutorial quest: plant a seed, then harvest a crop.</summary>
    public const int FirstHarvest = 1;

    private static readonly QuestDefinition[] s_all =
    [
        new(FirstHarvest, "first_harvest", new QuestObjective(QuestEventKind.CropPlanted, 0, 1), new QuestObjective(QuestEventKind.CropHarvested, 0, 1)),
    ];

    private static readonly Dictionary<int, QuestDefinition> s_byId = Index();

    /// <summary>Gets every quest, in id order.</summary>
    public static IReadOnlyList<QuestDefinition> All => s_all;

    /// <summary>Looks up a quest.</summary>
    /// <param name="questId">Quest id.</param>
    /// <param name="quest">The definition when found.</param>
    /// <returns><see langword="true"/> when the id names a quest.</returns>
    public static bool TryGet(int questId, [NotNullWhen(true)] out QuestDefinition? quest) => s_byId.TryGetValue(questId, out quest);

    private static Dictionary<int, QuestDefinition> Index()
    {
        Dictionary<int, QuestDefinition> index = new(s_all.Length);
        foreach (QuestDefinition quest in s_all)
        {
            index.Add(quest.Id, quest);
        }

        return index;
    }
}
