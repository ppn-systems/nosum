// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Common;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to create an account and sign the connection in.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class Register : PacketBase<Register>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.Register;

    /// <summary>Wanted login: a @gmail.com address, case-insensitive; see <see cref="AccountLimits"/>.</summary>
    [SerializeOrder(0)] public string Email { get; set; } = string.Empty;

    /// <summary>Wanted password in plain text; the transport protects the wire. Never logged.</summary>
    [SerializeOrder(1)] public string Password { get; set; } = string.Empty;

    /// <summary>Preferred language (the client's current UI language); <see cref="GameLanguage.Unspecified"/> means Vietnamese.</summary>
    [SerializeOrder(2)] public GameLanguage PreferredLanguage { get; set; }
}
