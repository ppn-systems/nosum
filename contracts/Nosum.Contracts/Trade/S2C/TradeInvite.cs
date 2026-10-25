// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Trade.S2C;

/// <summary>Pushed to one player: another player asks to trade.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeInvite : PacketBase<TradeInvite>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeInvite;

    /// <summary>The asking player's id on the map.</summary>
    [SerializeOrder(0)] public uint FromPlayerId { get; set; }
}
