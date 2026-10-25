// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to sign the connection in with an e-mail address and password.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class Login : PacketBase<Login>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.Login;

    /// <summary>Account e-mail (case-insensitive)</summary>
    [SerializeOrder(0)] public string Email { get; set; } = string.Empty;

    /// <summary>Account password in plain text; the transport protects the wire. Never logged.</summary>
    [SerializeOrder(1)] public string Password { get; set; } = string.Empty;
}
