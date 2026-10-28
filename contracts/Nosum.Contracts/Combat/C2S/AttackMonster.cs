// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Combat.C2S;

/// <summary>Attacks a monster on the player's current map with the weapon or staff in <see cref="Slot"/>. Reply: <see cref="S2C.AttackMonsterResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AttackMonster : PacketBase<AttackMonster>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.AttackMonster;

    /// <summary>Target monster id on the map.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(0)] public int MonsterId { get; set; }

    /// <summary>Hotbar/inventory slot holding the weapon or staff.</summary>
    [SerializeOrder(1)] public byte Slot { get; set; }
}
