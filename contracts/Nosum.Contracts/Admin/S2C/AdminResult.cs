// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Admin.S2C;

/// <summary>The reply to every admin intent except the two report reads.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminResult : PacketBase<AdminResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.AdminResult;

    /// <summary>Ok, or why the intent was refused.</summary>
    [SerializeOrder(0)] public AdminStatus Status { get; set; }
}
