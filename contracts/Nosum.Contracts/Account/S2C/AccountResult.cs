// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>The server's reply to <see cref="C2S.Register"/>, <see cref="C2S.Login"/> and <see cref="C2S.ResumeSession"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AccountResult : PacketBase<AccountResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.AccountResult;

    /// <summary>Outcome; the fields below are only meaningful for <see cref="AccountStatus.Ok"/>.</summary>
    [SerializeOrder(0)] public AccountStatus Status { get; set; }

    /// <summary>The signed-in player's id.</summary>
    [SerializeOrder(1)] public Guid PlayerId { get; set; }

    /// <summary>The player's username as registered.</summary>
    [SerializeOrder(2)] public string Username { get; set; } = string.Empty;

    /// <summary>Session token for <see cref="C2S.ResumeSession"/>. Keep it secret; empty unless the status is Ok.</summary>
    [SerializeOrder(3)] public string SessionToken { get; set; } = string.Empty;

    /// <summary>When the session token expires, Unix time in seconds (UTC).</summary>
    [SerializeOrder(4)] public long ExpiresAtUnixSeconds { get; set; }

    /// <summary>The player's stored preferred language (BCP-47 tag); the client applies it after sign-in.</summary>
    [SerializeOrder(5)] public string PreferredLanguage { get; set; } = string.Empty;
}
