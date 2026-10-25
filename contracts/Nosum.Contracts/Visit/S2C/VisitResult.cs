// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Visit.S2C;

/// <summary>Reply to a Visit request.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class VisitResult : PacketBase<VisitResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => VisitOpCode.VisitResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public VisitStatus Status { get; set; }
}
