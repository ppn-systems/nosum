// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Goals.S2C;

/// <summary>One goal of a snapshot. A plain serializable row: no opcode, no header. The target, item and reward are the shared catalogue's.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GoalRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static GoalRow Create() => new();

    /// <summary>The goal id.</summary>
    [SerializeOrder(0)] public int GoalId { get; set; }

    /// <summary>The season number the row belongs to (the goal can come round again in a later year).</summary>
    [SerializeOrder(1)] public int SeasonNumber { get; set; }

    /// <summary>How many items everybody delivered.</summary>
    [SerializeOrder(2)] public long Total { get; set; }

    /// <summary>Where the goal is in its life.</summary>
    [SerializeOrder(3)] public GoalState State { get; set; }

    /// <summary>How many items the viewer delivered.</summary>
    [SerializeOrder(4)] public long Mine { get; set; }

    /// <summary>How many players delivered anything.</summary>
    [SerializeOrder(5)] public int Contributors { get; set; }
}
