// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Coop.S2C;

/// <summary>One shared co-op objective and its progress. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CoopObjectiveRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static CoopObjectiveRow Create() => new();

    /// <summary>The objective id (<c>CoopObjectiveCatalog</c>); the client holds the title and target.</summary>
    [SerializeOrder(0)] public int ObjectiveId { get; set; }

    /// <summary>How many items the members have harvested toward it.</summary>
    [SerializeOrder(1)] public int Contributed { get; set; }

    /// <summary>Whether the co-op has earned the trophy.</summary>
    [SerializeOrder(2)] public bool Completed { get; set; }
}
