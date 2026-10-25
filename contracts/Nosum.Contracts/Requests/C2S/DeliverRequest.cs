// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

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
    [WireRange(0, int.MaxValue)]
    [SerializeOrder(0)] public int Day { get; set; }

    /// <summary>The request's slot in that day (the wire bound is far above the requests a day has; the server checks the slot).</summary>
    [WireRange(0, 255)]
    [SerializeOrder(1)] public int Slot { get; set; }
}
