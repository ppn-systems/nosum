// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Admin.C2S;

/// <summary>Admin intent: bans a player; their connection ends and they cannot sign in or resume until unbanned.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminBan : PacketBase<AdminBan>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.Ban;

    /// <summary>The nickname of the player to ban.</summary>
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
