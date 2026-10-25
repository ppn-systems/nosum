// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Common;

namespace Nosum.Contracts.Account.S2C;

/// <summary>The server's reply to <see cref="C2S.Register"/>, <see cref="C2S.Login"/> <see cref="C2S.ResumeSession"/>, <see cref="C2S.StartGuest"/>, <see cref="C2S.UpgradeGuest"/> and <see cref="C2S.Logout"/> (Ok, empty session fields).</summary>
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

    /// <summary>The login e-mail in lower case; empty for a guest.</summary>
    [SerializeOrder(2)] public string Email { get; set; } = string.Empty;

    /// <summary>Session token for <see cref="C2S.ResumeSession"/>. Keep it secret; empty unless the status is Ok.</summary>
    [SerializeOrder(3)] public string SessionToken { get; set; } = string.Empty;

    /// <summary>When the session token expires, Unix time in seconds (UTC).</summary>
    [SerializeOrder(4)] public long ExpiresAtUnixSeconds { get; set; }

    /// <summary>The player's stored language; the client applies it after sign-in.</summary>
    [SerializeOrder(5)] public GameLanguage PreferredLanguage { get; set; }

    /// <summary>True while the player is a guest (no e-mail or password yet).</summary>
    [SerializeOrder(6)] public bool IsGuest { get; set; }

    /// <summary>True once the game has unlocked the invitation to register (a guest who finished the first quest).</summary>
    [SerializeOrder(7)] public bool RegistrationPromptUnlocked { get; set; }

    /// <summary>The player's nickname: the chosen one, or a neutral guest name (<c>guest-1234</c>) the client renders with a localized prefix. Appended in protocol version 3.</summary>
    [SerializeOrder(8)] public string Nickname { get; set; } = string.Empty;
}
