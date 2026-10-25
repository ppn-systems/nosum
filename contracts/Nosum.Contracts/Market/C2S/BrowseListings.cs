// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Reads one page of active listings, newest first.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BrowseListings : PacketBase<BrowseListings>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.BrowseListings;

    /// <summary>Zero-based page number.</summary>
    [SerializeOrder(0)] public int Page { get; set; }

    /// <summary>Only listings of these items; empty for all.</summary>
    [SerializeOrder(1)] public List<int> ItemIds { get; set; } = [];
}
