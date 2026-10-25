// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Asks for the recent daily price history of one item.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetPriceHistory : PacketBase<GetPriceHistory>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.GetPriceHistory;

    /// <summary>The item.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(0)] public int ItemId { get; set; }
}
