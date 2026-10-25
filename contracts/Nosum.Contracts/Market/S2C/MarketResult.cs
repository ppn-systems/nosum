// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.S2C;

/// <summary>Reply to a listing request.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MarketResult : PacketBase<MarketResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.MarketResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public MarketStatus Status { get; set; }
}
