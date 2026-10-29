// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>Reply to <see cref="C2S.EquipBestiaryTitle"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EquipBestiaryTitleResult : PacketBase<EquipBestiaryTitleResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.EquipBestiaryTitleResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public BestiaryStatus Status { get; set; }
}
