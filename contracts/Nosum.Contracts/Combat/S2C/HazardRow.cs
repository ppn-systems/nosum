// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>One environmental hazard on a dungeon floor (docs/adr/ADR-061).</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class HazardRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static HazardRow Create() => new();

    /// <summary>Hazard id within the instance.</summary>
    [SerializeOrder(0)] public byte HazardId { get; set; }

    /// <summary>Hazard kind (1=SporeVent, 2=SpikeTrap, 3=MagmaFissure).</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>Tile X.</summary>
    [SerializeOrder(2)] public ushort X { get; set; }

    /// <summary>Tile Y.</summary>
    [SerializeOrder(3)] public ushort Y { get; set; }

    /// <summary>Base damage dealt per active contact tick.</summary>
    [SerializeOrder(4)] public byte Damage { get; set; }
}
