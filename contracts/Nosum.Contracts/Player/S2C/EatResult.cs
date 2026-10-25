// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Reply to <see cref="C2S.Eat"/>. The new energy arrives as <see cref="EnergyChanged"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EatResult : PacketBase<EatResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.EatResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public EatStatus Status { get; set; }
}
