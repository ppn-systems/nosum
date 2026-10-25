// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Trade.C2S;

/// <summary>Answers a trade invitation.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeRespond : PacketBase<TradeRespond>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeRespond;

    /// <summary>Whether to accept.</summary>
    [SerializeOrder(0)] public bool Accept { get; set; }
}
