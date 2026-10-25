// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Building.S2C;

/// <summary>One farm building of a land (docs/adr/ADR-028).</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BuildingRow
{
    /// <summary>Creates an empty row.</summary>
    /// <returns>The row.</returns>
    public static BuildingRow Create() => new();

    /// <summary>The building's id; its interior instance is keyed by it.</summary>
    [SerializeOrder(0)] public Guid Id { get; set; }

    /// <summary>The placed object that stands on the land.</summary>
    [SerializeOrder(1)] public int ObjectId { get; set; }

    /// <summary>The building kind.</summary>
    [SerializeOrder(2)] public byte Kind { get; set; }

    /// <summary>The land's map id (<c>farm</c> or <c>coopfarm</c>).</summary>
    [SerializeOrder(3)] public string MapId { get; set; } = string.Empty;

    /// <summary>Milliseconds of construction left; zero once finished.</summary>
    [SerializeOrder(4)] public int RemainingMs { get; set; }

    /// <summary>Animals living in it.</summary>
    [SerializeOrder(5)] public byte Animals { get; set; }

    /// <summary>Animals it holds.</summary>
    [SerializeOrder(6)] public byte Capacity { get; set; }
}
