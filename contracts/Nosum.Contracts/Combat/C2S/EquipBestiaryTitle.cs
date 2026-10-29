// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.C2S;

/// <summary>
/// Equips the slayer title of a species the player has mastered, or takes the title off when <see cref="Species"/> is zero (docs/adr/ADR-068).
/// Reply: <see cref="S2C.EquipBestiaryTitleResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EquipBestiaryTitle : PacketBase<EquipBestiaryTitle>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.EquipBestiaryTitle;

    /// <summary>The monster species as its <c>MonsterKind</c> value; zero takes the title off.</summary>
    [SerializeOrder(0)] public byte Species { get; set; }
}
