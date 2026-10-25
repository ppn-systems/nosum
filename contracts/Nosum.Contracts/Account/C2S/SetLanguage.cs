// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Common;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to store the signed-in player's preferred language.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SetLanguage : PacketBase<SetLanguage>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.SetLanguage;

    /// <summary>The new language; <see cref="GameLanguage.Unspecified"/> means Vietnamese. An undefined value is refused.</summary>
    [WireDefined]
    [SerializeOrder(0)] public GameLanguage Language { get; set; }
}
