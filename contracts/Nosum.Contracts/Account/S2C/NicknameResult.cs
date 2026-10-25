// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>The server's reply to <see cref="C2S.SetNickname"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class NicknameResult : PacketBase<NicknameResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.NicknameResult;

    /// <summary>Outcome: Ok, InvalidNickname, NicknameReserved, NicknameTaken or NicknameTooSoon.</summary>
    [SerializeOrder(0)] public AccountStatus Status { get; set; }

    /// <summary>The player's nickname after the request (the new one on Ok, the unchanged one otherwise).</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;

    /// <summary>When the next change is allowed, Unix milliseconds on the server clock; zero when a change is allowed now.</summary>
    [SerializeOrder(2)] public long NextChangeAtMs { get; set; }
}
