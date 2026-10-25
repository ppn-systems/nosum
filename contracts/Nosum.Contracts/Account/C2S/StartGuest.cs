// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Common;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to create a guest player and sign the connection in as it.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class StartGuest : PacketBase<StartGuest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.StartGuest;

    /// <summary>Preferred language; <see cref="GameLanguage.Unspecified"/> means Vietnamese.</summary>
    [WireDefined]
    [SerializeOrder(0)] public GameLanguage PreferredLanguage { get; set; }
}
