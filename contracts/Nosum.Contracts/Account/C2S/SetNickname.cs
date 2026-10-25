// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to change the signed-in player's nickname. Allowed once every 24 hours.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SetNickname : PacketBase<SetNickname>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.SetNickname;

    /// <summary>The wanted nickname.</summary>
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}
