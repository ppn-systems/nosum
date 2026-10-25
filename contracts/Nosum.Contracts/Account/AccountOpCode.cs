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

    /// <summary>Signs in with an e-mail and password.</summary>
    public const ushort Login = 0x1002;

    /// <summary>Signs in again with a session token issued by an earlier register or login.</summary>
    public const ushort ResumeSession = 0x1003;

    /// <summary>Changes the signed-in player's preferred language.</summary>
    public const ushort SetLanguage = 0x1004;

    /// <summary>Starts playing as a guest: no e-mail or password, the server creates a guest player and a session.</summary>
    public const ushort StartGuest = 0x1005;

    /// <summary>Turns the signed-in guest into a registered player in place (same player id and data).</summary>
    public const ushort UpgradeGuest = 0x1006;

    /// <summary>Ends the session: revokes its token and signs the connection out.</summary>
    public const ushort Logout = 0x1007;

    /// <summary>The reply to <see cref="Register"/>, <see cref="Login"/>, <see cref="ResumeSession"/>, <see cref="StartGuest"/>, <see cref="UpgradeGuest"/> and <see cref="Logout"/>.</summary>
    public const ushort AccountResult = 0x1081;

    /// <summary>Server push: the session was ended by the server (see <see cref="AccountStatus.SessionReplaced"/>).</summary>
    public const ushort SessionEnded = 0x1084;

    /// <summary>The reply to <see cref="SetLanguage"/>.</summary>
    public const ushort LanguageResult = 0x1082;

    /// <summary>Server push: the guest finished the first quest and may now be invited to register.</summary>
    public const ushort RegistrationPromptUnlocked = 0x1085;
}
