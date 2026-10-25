// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Quest.S2C;

/// <summary>One quest of a player: the quest id from the catalogue, its state and the count of each objective. No text: the client looks the quest up by id.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class QuestRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static QuestRow Create() => new();

    /// <summary>Quest id from the quest catalogue.</summary>
    [SerializeOrder(0)] public int QuestId { get; set; }

    /// <summary>Whether the quest is in progress or done.</summary>
    [SerializeOrder(1)] public QuestState State { get; set; }

    /// <summary>How many each objective has counted, in the catalogue's objective order.</summary>
    [SerializeOrder(2)] public List<int> Progress { get; set; } = [];
}
