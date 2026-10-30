// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Fishing.S2C;

/// <summary>
/// Reply to <see cref="C2S.ReelIn"/>. A caught fish is already in the inventory when this arrives; the inventory and skill pushes carry the
/// rest of the change.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ReelResult : PacketBase<ReelResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FishingOpCode.ReelResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public FishingStatus Status { get; set; }

    /// <summary>The item caught; zero when nothing was caught.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many were caught: one for a fish, otherwise zero.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>Fishing experience the attempt gave.</summary>
    [SerializeOrder(3)] public int XpGained { get; set; }

    /// <summary>The fish's size in centimetres, rolled by the server from the cast seed; zero when nothing was caught.</summary>
    [SerializeOrder(4)] public int SizeCm { get; set; }
}
