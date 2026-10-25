// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Market.C2S;

/// <summary>Withdraws the player's own listing.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CancelListing : PacketBase<CancelListing>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MarketOpCode.CancelListing;

    /// <summary>The listing.</summary>
    [SerializeOrder(0)] public Guid ListingId { get; set; }
}
