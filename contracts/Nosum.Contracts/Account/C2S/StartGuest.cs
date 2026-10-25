// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to create a guest player and sign the connection in as it.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class StartGuest : PacketBase<StartGuest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.StartGuest;

    /// <summary>Preferred language as a BCP-47 tag; empty means <see cref="AccountLimits.DefaultLanguage"/>.</summary>
    [SerializeOrder(0)] public string PreferredLanguage { get; set; } = string.Empty;
}
