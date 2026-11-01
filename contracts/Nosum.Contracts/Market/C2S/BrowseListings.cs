// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Reads one page of active listings, in the order of <see cref="SortBy"/> (newest first by default).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BrowseListings : PacketBase<BrowseListings>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.BrowseListings;

    /// <summary>Zero-based page number.</summary>
    [WireRange(0, WireLimits.MaxPage)]
    [SerializeOrder(0)] public int Page { get; set; }

    /// <summary>Only listings of these items; empty for all.</summary>
    [WireMaxLength(WireLimits.MaxFilterEntries)]
    [SerializeOrder(1)] public List<int> ItemIds { get; set; } = [];

    /// <summary>The order of the page: a <see cref="Market.MarketSort"/> value. Appended in protocol 29 (docs/adr/ADR-013 amendment); an older client that sends no sort is not supported: server and client ship together.</summary>
    [WireRange(0, 2)]
    [SerializeOrder(2)] public int SortBy { get; set; }

    /// <summary>
    /// Listings on the page: the rows the client's panel has room for (docs/adr/ADR-101 amendment 2026-11-01). Zero asks for the server's default (10);
    /// a larger value is clamped to <see cref="WireLimits.MaxListingsPageSize"/>. Appended in protocol 34.
    /// </summary>
    [WireRange(0, WireLimits.MaxListingsPageSize)]
    [SerializeOrder(3)] public int PageSize { get; set; }
}
