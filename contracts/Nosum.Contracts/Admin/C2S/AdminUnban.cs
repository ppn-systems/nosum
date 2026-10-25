// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Admin.C2S;

/// <summary>Admin intent: lifts a ban.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminUnban : PacketBase<AdminUnban>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.Unban;

    /// <summary>The nickname of the banned player.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
