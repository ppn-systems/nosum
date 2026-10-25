// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Leaves the co-op.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LeaveCoop : PacketBase<LeaveCoop>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.LeaveCoop;
}
