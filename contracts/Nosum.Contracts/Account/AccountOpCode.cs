// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>
/// Account protocol opcodes. Band: <c>0x1000-0x10FF</c> (C2S requests <c>0x1001-0x107F</c>,
/// S2C replies <c>0x1081-0x10FF</c>). Never reassign or reuse a value.
/// <para>Password accounts were removed (docs/adr/ADR-069): <c>Register</c>, <c>Login</c>, <c>UpgradeGuest</c>, <c>ChangePassword</c>,
/// <c>PasswordResult</c> and the recovery values <c>0x11EF</c>, <c>0x11F0</c> are retired and reserved. Players sign in with Google or
/// Microsoft through <see cref="ExternalSignIn"/> and <see cref="LinkExternalIdentity"/>. Operations (docs/adr/ADR-031) take theirs upward from <c>0x1100</c>.</para>
/// </summary>
public static class AccountOpCode
{
    /// <summary>Retired: password registration (docs/adr/ADR-069). Reserved: never reuse this value.</summary>
    public const ushort Register = 0x1001;

    /// <summary>Retired: password sign-in (docs/adr/ADR-069). Reserved: never reuse this value.</summary>
    public const ushort Login = 0x1002;

    /// <summary>Signs in again with a session token issued by an earlier sign-in.</summary>
    public const ushort ResumeSession = 0x1003;

    /// <summary>Changes the signed-in player's preferred language.</summary>
    public const ushort SetLanguage = 0x1004;

    /// <summary>Starts playing as a guest: no identity, the server creates a guest player and a session.</summary>
    public const ushort StartGuest = 0x1005;

    /// <summary>Retired: password upgrade of a guest (docs/adr/ADR-069); the guest links a provider identity with <see cref="LinkExternalIdentity"/>. Reserved: never reuse this value.</summary>
    public const ushort UpgradeGuest = 0x1006;

    /// <summary>Ends the session: revokes its token and signs the connection out.</summary>
    public const ushort Logout = 0x1007;

    /// <summary>Changes the signed-in player's nickname (at most once per 24 hours). Reply: <see cref="NicknameResult"/>.</summary>
    public const ushort SetNickname = 0x1008;

    /// <summary>
    /// Signs in with a Google or Microsoft ID token, creating the account on first use. Reply: <see cref="AccountResult"/>.
    /// </summary>
    public const ushort ExternalSignIn = 0x1009;

    /// <summary>
    /// Turns the signed-in guest into a registered player by linking a Google or Microsoft identity in place (same player id and data).
    /// Reply: <see cref="AccountResult"/>.
    /// </summary>
    public const ushort LinkExternalIdentity = 0x100A;

    /// <summary>The reply to <see cref="ExternalSignIn"/>, <see cref="LinkExternalIdentity"/>, <see cref="ResumeSession"/>, <see cref="StartGuest"/> and <see cref="Logout"/>.</summary>
    public const ushort AccountResult = 0x1081;

    /// <summary>Server push: the session was ended by the server (see <see cref="AccountStatus.SessionReplaced"/>).</summary>
    public const ushort SessionEnded = 0x1084;

    /// <summary>The reply to <see cref="SetLanguage"/>.</summary>
    public const ushort LanguageResult = 0x1082;

    /// <summary>The reply to <see cref="SetNickname"/>.</summary>
    public const ushort NicknameResult = 0x1086;

    /// <summary>Server push: the guest finished the first quest and may now be invited to register.</summary>
    public const ushort RegistrationPromptUnlocked = 0x1085;

    /// <summary>Retired with account recovery (docs/adr/ADR-032, removed in docs/adr/ADR-069). Reserved: never reuse this value.</summary>
    public const ushort RequestPasswordReset = 0x11F0;

    /// <summary>Retired with account recovery (docs/adr/ADR-032, removed in docs/adr/ADR-069). Reserved: never reuse this value.</summary>
    public const ushort ConfirmPasswordReset = 0x11EF;

    /// <summary>Retired: password change (docs/adr/ADR-069). Reserved: never reuse this value.</summary>
    public const ushort ChangePassword = 0x11EE;

    /// <summary>Retired: the reply to the password change (docs/adr/ADR-069). Reserved: never reuse this value.</summary>
    public const ushort PasswordResult = 0x11E0;
}
