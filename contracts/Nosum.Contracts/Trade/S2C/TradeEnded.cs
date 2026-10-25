// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Trade.S2C;

/// <summary>Pushed to both parties when the trade ends.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeEnded : PacketBase<TradeEnded>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeEnded;

    /// <summary>Why.</summary>
    [SerializeOrder(0)] public TradeEndReason Reason { get; set; }
}
