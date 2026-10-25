// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>
/// Account protocol opcodes. Band: <c>0x1000-0x10FF</c> (C2S requests <c>0x1001-0x107F</c>,
/// S2C replies <c>0x1081-0x10FF</c>). Never reassign or reuse a value.
/// <para>Account recovery (docs/adr/ADR-032) takes its values from the top of the free range above the band,
/// downward from <c>0x11F0</c>; operations (docs/adr/ADR-031) take theirs upward from <c>0x1100</c>.</para>
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

    /// <summary>Changes the signed-in player's nickname (at most once per 24 hours). Reply: <see cref="NicknameResult"/>.</summary>
    public const ushort SetNickname = 0x1008;

    /// <summary>The reply to <see cref="Register"/>, <see cref="Login"/>, <see cref="ResumeSession"/>, <see cref="StartGuest"/>, <see cref="UpgradeGuest"/> and <see cref="Logout"/>.</summary>
    public const ushort AccountResult = 0x1081;

    /// <summary>Server push: the session was ended by the server (see <see cref="AccountStatus.SessionReplaced"/>).</summary>
    public const ushort SessionEnded = 0x1084;

    /// <summary>The reply to <see cref="SetLanguage"/>.</summary>
    public const ushort LanguageResult = 0x1082;

    /// <summary>The reply to <see cref="SetNickname"/>.</summary>
    public const ushort NicknameResult = 0x1086;

    /// <summary>Server push: the guest finished the first quest and may now be invited to register.</summary>
    public const ushort RegistrationPromptUnlocked = 0x1085;

    /// <summary>Asks for a one-time recovery code by e-mail. The reply is always <see cref="PasswordResult"/> with Ok unless throttled, whether or not the address has an account.</summary>
    public const ushort RequestPasswordReset = 0x11F0;

    /// <summary>Sets a new password with the e-mailed code and ends every session of the account. Reply: <see cref="PasswordResult"/>.</summary>
    public const ushort ConfirmPasswordReset = 0x11EF;

    /// <summary>Changes the signed-in player's password and ends every other session. Reply: <see cref="PasswordResult"/>.</summary>
    public const ushort ChangePassword = 0x11EE;

    /// <summary>The reply to <see cref="RequestPasswordReset"/>, <see cref="ConfirmPasswordReset"/> and <see cref="ChangePassword"/>.</summary>
    public const ushort PasswordResult = 0x11E0;
}
