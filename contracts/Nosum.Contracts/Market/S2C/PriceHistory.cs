// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.S2C;

/// <summary>Reply to <see cref="C2S.GetPriceHistory"/>: the days of the last two weeks that had sales, oldest first.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PriceHistory : PacketBase<PriceHistory>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.PriceHistory;

    /// <summary>The item the history is of.</summary>
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>The game day number now, so the client can place the days on a time axis.</summary>
    [SerializeOrder(1)] public int Today { get; set; }

    /// <summary>The days that had sales, oldest first.</summary>
    [SerializeOrder(2)] public List<PriceDayRow> Days { get; set; } = [];

    /// <summary>The mean unit price of the sales of the last seven game days, coins; zero when none (docs/adr/ADR-030 D4).</summary>
    [SerializeOrder(3)] public long Average { get; set; }
}
