// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Requests.C2S;

/// <summary>Fills one of today's requests: the server takes the requested items and pays the reward.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DeliverRequest : PacketBase<DeliverRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => RequestsOpCode.DeliverRequest;

    /// <summary>The game day the player saw the request on; refused when it is no longer the current day.</summary>
    [SerializeOrder(0)] public int Day { get; set; }

    /// <summary>The request's slot in that day.</summary>
    [SerializeOrder(1)] public int Slot { get; set; }
}
