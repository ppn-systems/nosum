// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Trade.C2S;

/// <summary>Asks a player on the same map to trade.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeRequest : PacketBase<TradeRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeRequest;

    /// <summary>The other player's id on the map.</summary>
    [SerializeOrder(0)] public uint TargetPlayerId { get; set; }
}
