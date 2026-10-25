// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.S2C;

/// <summary>Reply to <see cref="C2S.ShipItems"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ShipResult : PacketBase<ShipResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.ShipResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public MarketStatus Status { get; set; }

    /// <summary>Coins paid; zero unless the status is Ok.</summary>
    [SerializeOrder(1)] public long Earned { get; set; }
}
