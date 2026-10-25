// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>
/// Account protocol opcodes. Band: <c>0x1000-0x10FF</c> (C2S requests <c>0x1001-0x107F</c>,
/// S2C replies <c>0x1081-0x10FF</c>). Never reassign or reuse a value.
/// </summary>
public static class AccountOpCode
{
    /// <summary>Creates an account and signs it in.</summary>
    public const ushort Register = 0x1001;

    /// <summary>Signs in with a username and password.</summary>
    public const ushort Login = 0x1002;

    /// <summary>Signs in again with a session token issued by an earlier register or login.</summary>
    public const ushort ResumeSession = 0x1003;

    /// <summary>Changes the signed-in player's preferred language.</summary>
    public const ushort SetLanguage = 0x1004;

    /// <summary>The reply to <see cref="Register"/>, <see cref="Login"/> and <see cref="ResumeSession"/>.</summary>
    public const ushort AccountResult = 0x1081;

    /// <summary>The reply to <see cref="SetLanguage"/>.</summary>
    public const ushort LanguageResult = 0x1082;
}
