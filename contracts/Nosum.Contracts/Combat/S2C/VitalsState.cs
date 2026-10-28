// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Current player vitals (HP, MaxHp, MP, MaxMp) and cumulative Combat XP.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class VitalsState : PacketBase<VitalsState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.VitalsState;

    /// <summary>Current health points.</summary>
    [SerializeOrder(0)] public ushort Hp { get; set; }

    /// <summary>Maximum health points.</summary>
    [SerializeOrder(1)] public ushort MaxHp { get; set; }

    /// <summary>Current mana points.</summary>
    [SerializeOrder(2)] public ushort Mp { get; set; }

    /// <summary>Maximum mana points.</summary>
    [SerializeOrder(3)] public ushort MaxMp { get; set; }

    /// <summary>Cumulative Combat skill experience.</summary>
    [SerializeOrder(4)] public int CombatXp { get; set; }
}
