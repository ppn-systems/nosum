// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Node.C2S;

/// <summary>Asks for the resource nodes of the place the player stands in. Send it after joining a map.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetNodes : PacketBase<GetNodes>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => NodeOpCode.GetNodes;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}
