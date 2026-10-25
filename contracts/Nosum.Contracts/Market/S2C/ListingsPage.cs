// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.S2C;

/// <summary>One page of listings.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ListingsPage : PacketBase<ListingsPage>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.ListingsPage;

    /// <summary>The server clock when read, Unix milliseconds.</summary>
    [SerializeOrder(0)] public long ServerNowMs { get; set; }

    /// <summary>Page number, zero based.</summary>
    [SerializeOrder(1)] public int Page { get; set; }

    /// <summary>Number of listings that match, all pages.</summary>
    [SerializeOrder(2)] public int Total { get; set; }

    /// <summary>The listings of this page.</summary>
    [SerializeOrder(3)] public List<ListingRow> Rows { get; set; } = [];
}
