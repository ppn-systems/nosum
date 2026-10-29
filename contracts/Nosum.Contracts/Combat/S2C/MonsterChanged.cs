// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Push when a monster takes damage or is defeated (<see cref="Hp"/> == 0).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MonsterChanged : PacketBase<MonsterChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.MonsterChanged;

    /// <summary>Monster id within the map/floor.</summary>
    [SerializeOrder(0)] public int MonsterId { get; set; }

    /// <summary>Monster species (<c>MonsterKind</c>).</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>Tile X.</summary>
    [SerializeOrder(2)] public ushort X { get; set; }

    /// <summary>Tile Y.</summary>
    [SerializeOrder(3)] public ushort Y { get; set; }

    /// <summary>Remaining HP (0 when defeated).</summary>
    [SerializeOrder(4)] public ushort Hp { get; set; }

    /// <summary>Maximum HP.</summary>
    [SerializeOrder(5)] public ushort MaxHp { get; set; }

    /// <summary>Elite affix modifier (docs/adr/ADR-058).</summary>
    [SerializeOrder(6)] public byte Modifier { get; set; }

    /// <summary>Whether the monster is enraged (1) at or below its species HP threshold (docs/adr/ADR-047).</summary>
    [SerializeOrder(7)] public byte Enraged { get; set; }
}
