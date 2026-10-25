// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Asks the server to store the signed-in player's preferred language.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SetLanguage : PacketBase<SetLanguage>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.SetLanguage;

    /// <summary>BCP-47 language tag, for example <c>vi</c> or <c>en</c>.</summary>
    [SerializeOrder(0)] public string Language { get; set; } = string.Empty;
}
