// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Look.C2S;

/// <summary>Asks for the looks of everyone on the map.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetLooks : PacketBase<GetLooks>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => LookOpCode.GetLooks;
}
