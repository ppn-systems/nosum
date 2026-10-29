// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Requests.S2C;

/// <summary>Reply to <see cref="C2S.DeliverRequest"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RequestResult : PacketBase<RequestResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => RequestsOpCode.RequestResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public RequestStatus Status { get; set; }

    /// <summary>The coins that were paid, the early bonus included; zero unless the status is Ok.</summary>
    [SerializeOrder(1)] public long Reward { get; set; }

    /// <summary>Whether the delivery was in the early window and was paid the bonus; false unless the status is Ok.</summary>
    [SerializeOrder(2)] public bool Bonus { get; set; }
}
