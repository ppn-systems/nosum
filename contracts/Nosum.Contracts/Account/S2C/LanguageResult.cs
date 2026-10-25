// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>The server's reply to <see cref="C2S.SetLanguage"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LanguageResult : PacketBase<LanguageResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.LanguageResult;

    /// <summary>Outcome code: Ok or InvalidLanguage.</summary>
    [SerializeOrder(0)] public AccountStatus Status { get; set; }

    /// <summary>The language now stored for the player.</summary>
    [SerializeOrder(1)] public string Language { get; set; } = string.Empty;
}
