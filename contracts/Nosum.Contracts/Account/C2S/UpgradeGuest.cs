// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to give the signed-in guest an e-mail login and password. Only valid on a guest session; the player id and data stay.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UpgradeGuest : PacketBase<UpgradeGuest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.UpgradeGuest;

    /// <summary>Wanted login: a @gmail.com address.</summary>
    [SerializeOrder(0)] public string Email { get; set; } = string.Empty;

    /// <summary>Wanted password in plain text; never logged.</summary>
    [SerializeOrder(1)] public string Password { get; set; } = string.Empty;

    /// <summary>Wanted display nickname, replacing the guest's neutral one. Appended in protocol version 3.</summary>
    [SerializeOrder(2)] public string Nickname { get; set; } = string.Empty;
}
