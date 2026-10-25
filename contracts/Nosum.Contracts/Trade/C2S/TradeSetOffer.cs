// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Trade.S2C;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Trade.C2S;

/// <summary>Replaces the player's side of the trade.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeSetOffer : PacketBase<TradeSetOffer>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => TradeOpCode.TradeSetOffer;

    /// <summary>Offered items.</summary>
    [WireMaxLength(WireLimits.MaxOfferLines)]
    [SerializeOrder(0)] public List<TradeItemRow> Items { get; set; } = [];

    /// <summary>Offered coins.</summary>
    [WireRange(0, WireLimits.MaxCoins)]
    [SerializeOrder(1)] public long Coins { get; set; }
}
