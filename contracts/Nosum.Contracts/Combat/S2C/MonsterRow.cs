// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>One monster on a map or mine floor.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MonsterRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static MonsterRow Create() => new();

    /// <summary>Monster id within the map/floor instance.</summary>
    [SerializeOrder(0)] public int MonsterId { get; set; }

    /// <summary>Monster species (<c>MonsterKind</c> byte value).</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>Tile X.</summary>
    [SerializeOrder(2)] public ushort X { get; set; }

    /// <summary>Tile Y.</summary>
    [SerializeOrder(3)] public ushort Y { get; set; }

    /// <summary>Current HP.</summary>
    [SerializeOrder(4)] public ushort Hp { get; set; }

    /// <summary>Maximum HP.</summary>
    [SerializeOrder(5)] public ushort MaxHp { get; set; }

    /// <summary>Elite affix modifier (docs/adr/ADR-058).</summary>
    [SerializeOrder(6)] public byte Modifier { get; set; }
}
