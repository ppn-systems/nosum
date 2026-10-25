// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Handlers.Session;
using Nosum.Abstractions.Look;

namespace Nosum.Handlers.Account;

/// <summary>
/// Frames that carry a password or a session token are refused unless they are encrypted (<see cref="PacketEncryptionAttribute"/>).
/// Guest, register, login, resume, upgrade, logout and language preference. Guest, register, login and resume are public; the rest need a signed-in player.
/// </summary>
[PacketHandler("Nosum.Account")]
public static partial class AccountHandlers
{
    [Inject] private static IAccountService s_accounts = null!;
    [Inject] private static ISessionRegistry s_sessions = null!;
    [Inject] private static ILookService s_looks = null!;
    [Inject] private static IAbuseGuard s_abuse = null!;

    /// <summary>New accounts (registered or guest) one address may create per day; more are refused with <see cref="AccountStatus.TooManyRequests"/> (docs/adr/ADR-032 D6).</summary>
    internal const int NewAccountsPerAddressPerDay = 30;

    /// <summary>Recovery codes one address may ask for per hour.</summary>
    internal const int ResetRequestsPerAddressPerHour = 5;

    /// <summary>Recovery codes one address may try per hour, across all accounts.</summary>
    internal const int ResetConfirmsPerAddressPerHour = 20;

    /// <summary>Creates an account and signs the connection in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.Register)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleRegisterAsync(IPacketContext<Register> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string email = context.Packet.Email;
        string password = context.Packet.Password;
        GameLanguage language = context.Packet.PreferredLanguage;
        string nickname = context.Packet.Nickname;

        if (!s_abuse.TryAcquire("new-account", AddressOf(context.Connection), NewAccountsPerAddressPerDay, TimeSpan.FromDays(1)))
        {
            await ReplyOutcomeAsync(context.Sender, AccountOutcome.Refused(AccountStatus.TooManyRequests)).ConfigureAwait(false);
            return;
        }

        AccountOutcome outcome = await s_accounts.RegisterAsync(email, password, nickname, language, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
    }

    /// <summary>Creates a guest player and signs the connection in as it.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.StartGuest)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 3, PolicyId = "account-guest", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleStartGuestAsync(IPacketContext<StartGuest> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameLanguage language = context.Packet.PreferredLanguage;

        if (!s_abuse.TryAcquire("new-account", AddressOf(context.Connection), NewAccountsPerAddressPerDay, TimeSpan.FromDays(1)))
        {
            await ReplyOutcomeAsync(context.Sender, AccountOutcome.Refused(AccountStatus.TooManyRequests)).ConfigureAwait(false);
            return;
        }

        AccountOutcome outcome = await s_accounts.StartGuestAsync(language, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
    }

    /// <summary>Turns the signed-in guest into a registered player, in place.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.UpgradeGuest)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleUpgradeGuestAsync(IPacketContext<UpgradeGuest> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerIdentity player = context.Connection.GetPlayer();
        string email = context.Packet.Email;
        string password = context.Packet.Password;
        string nickname = context.Packet.Nickname;

        AccountOutcome outcome = await s_accounts
            .UpgradeGuestAsync(player.PlayerId, player.SessionId, email, password, nickname, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
        if (outcome.Status == AccountStatus.Ok)
        {
            // The guest chose a nickname by registering: everyone on the map learns it through the look row, like after a rename.
            await s_looks.AnnounceAsync(context.Connection, outcome.PlayerId, context.CancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Ends the session: revokes its token and signs the connection out.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.Logout)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(2, 5, PolicyId = "account-logout")]
    public static async ValueTask HandleLogoutAsync(IPacketContext<Logout> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerIdentity player = context.Connection.GetPlayer();

        await s_accounts.LogoutAsync(player.SessionId, context.CancellationToken).ConfigureAwait(false);
        s_sessions.Release(context.Connection);
        context.Connection.DetachPlayer();
        await ReplyOutcomeAsync(context.Sender, new AccountOutcome(AccountStatus.Ok, Guid.Empty, string.Empty, string.Empty, Guid.Empty, default, GameLanguage.Unspecified, false, false, string.Empty)).ConfigureAwait(false);
    }

    /// <summary>Signs the connection in with an e-mail and password.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.Login)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleLoginAsync(IPacketContext<Login> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string email = context.Packet.Email;
        string password = context.Packet.Password;

        AccountOutcome outcome = await s_accounts.LoginAsync(email, password, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
    }

    /// <summary>Signs the connection in again with a session token.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.ResumeSession)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(2, 10, PolicyId = "account-resume", HardLockoutSeconds = 60, MaxSoftViolations = 20)]
    public static async ValueTask HandleResumeAsync(IPacketContext<ResumeSession> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string token = context.Packet.SessionToken;

        AccountOutcome outcome = await s_accounts.ResumeAsync(token, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
    }

    /// <summary>Stores the signed-in player's preferred language.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.SetLanguage)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(2, 5, PolicyId = "account-language")]
    public static async ValueTask HandleSetLanguageAsync(IPacketContext<SetLanguage> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerIdentity player = context.Connection.GetPlayer();
        GameLanguage language = context.Packet.Language;

        AccountStatus status = await s_accounts.SetLanguageAsync(player.PlayerId, language, context.CancellationToken).ConfigureAwait(false);

        PacketScope<LanguageResult> lease = PacketFactory<LanguageResult>.Acquire();
        try
        {
            lease.Value.Status = status;
            lease.Value.Language = status == AccountStatus.Ok ? GameLanguages.Resolve(language) : GameLanguage.Unspecified;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Changes the signed-in player's nickname and tells their map.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.SetNickname)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(1, 5, PolicyId = "account-nickname", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleSetNicknameAsync(IPacketContext<SetNickname> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerIdentity player = context.Connection.GetPlayer();
        string nickname = context.Packet.Nickname;

        NicknameOutcome outcome = await s_accounts.SetNicknameAsync(player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        if (outcome.Status == AccountStatus.Ok)
        {
            // Everyone on the map learns the new name through the look row (docs/adr/ADR-009 amendments).
            await s_looks.AnnounceAsync(context.Connection, player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        }

        PacketScope<NicknameResult> lease = PacketFactory<NicknameResult>.Acquire();
        try
        {
            lease.Value.Status = outcome.Status;
            lease.Value.Nickname = outcome.Nickname;
            lease.Value.NextChangeAtMs = outcome.NextChangeAt?.ToUnixTimeMilliseconds() ?? 0;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Starts account recovery: e-mails a one-time code to a registered account. Answers Ok whether or not the address has an account.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.RequestPasswordReset)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 3, PolicyId = "account-recovery", HardLockoutSeconds = 60, MaxSoftViolations = 5)]
    public static async ValueTask HandleRequestPasswordResetAsync(IPacketContext<RequestPasswordReset> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string email = context.Packet.Email;

        AccountStatus status = s_abuse.TryAcquire("reset-request", AddressOf(context.Connection), ResetRequestsPerAddressPerHour, TimeSpan.FromHours(1))
            ? await s_accounts.RequestPasswordResetAsync(email, context.CancellationToken).ConfigureAwait(false)
            : AccountStatus.TooManyRequests;
        await ReplyPasswordAsync(context.Sender, status).ConfigureAwait(false);
    }

    /// <summary>Sets a new password with the e-mailed code and ends every session of the account, including a live connection.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.ConfirmPasswordReset)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 3, PolicyId = "account-recovery-confirm", HardLockoutSeconds = 60, MaxSoftViolations = 5)]
    public static async ValueTask HandleConfirmPasswordResetAsync(IPacketContext<ConfirmPasswordReset> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string email = context.Packet.Email;
        string code = context.Packet.Code;
        string newPassword = context.Packet.NewPassword;

        if (!s_abuse.TryAcquire("reset-confirm", AddressOf(context.Connection), ResetConfirmsPerAddressPerHour, TimeSpan.FromHours(1)))
        {
            await ReplyPasswordAsync(context.Sender, AccountStatus.TooManyRequests).ConfigureAwait(false);
            return;
        }

        PasswordResetOutcome outcome = await s_accounts.ConfirmPasswordResetAsync(email, code, newPassword, context.CancellationToken).ConfigureAwait(false);
        if (outcome.Status == AccountStatus.Ok)
        {
            // Every session ends, a connection that is still open included.
            await s_sessions.EndAsync(outcome.PlayerId, AccountStatus.SessionRevoked, context.CancellationToken).ConfigureAwait(false);
        }

        await ReplyPasswordAsync(context.Sender, outcome.Status).ConfigureAwait(false);
    }

    /// <summary>Changes the signed-in player's password; every other session token of the account stops working.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.ChangePassword)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleChangePasswordAsync(IPacketContext<ChangePassword> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerIdentity player = context.Connection.GetPlayer();
        string current = context.Packet.CurrentPassword;
        string next = context.Packet.NewPassword;

        AccountStatus status = await s_accounts.ChangePasswordAsync(player.PlayerId, player.SessionId, current, next, context.CancellationToken).ConfigureAwait(false);
        await ReplyPasswordAsync(context.Sender, status).ConfigureAwait(false);
    }

    // The address limits are keyed by (a loopback address is exempt in the guard); an unreadable endpoint shares one key rather than skipping the limit.
    private static string AddressOf(IConnection connection) => connection.NetworkEndpoint?.Address ?? "unknown";

    private static async ValueTask ReplyPasswordAsync(IPacketSender sender, AccountStatus status)
    {
        PacketScope<PasswordResult> lease = PacketFactory<PasswordResult>.Acquire();
        try
        {
            lease.Value.Status = status;
            await sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    // Signs the connection in on success (identity from the server's own outcome, never from the packet), then replies.
    private static async ValueTask CompleteAsync(IConnection connection, IPacketSender sender, AccountOutcome outcome)
    {
        if (outcome.Status == AccountStatus.Ok)
        {
            // One active connection per player: an older one is saved, told and closed before this one goes on.
            await s_sessions.ActivateAsync(outcome.PlayerId, connection).ConfigureAwait(false);
            connection.AttachPlayer(new PlayerIdentity(outcome.PlayerId, outcome.SessionId));
        }

        await ReplyOutcomeAsync(sender, outcome).ConfigureAwait(false);
    }

    private static async ValueTask ReplyOutcomeAsync(IPacketSender sender, AccountOutcome outcome)
    {
        PacketScope<AccountResult> lease = PacketFactory<AccountResult>.Acquire();
        try
        {
            AccountResult packet = lease.Value;
            packet.Status = outcome.Status;
            packet.PlayerId = outcome.PlayerId;
            packet.Email = outcome.Email;
            packet.SessionToken = outcome.SessionToken;
            packet.ExpiresAtUnixSeconds = outcome.Status == AccountStatus.Ok ? outcome.ExpiresAt.ToUnixTimeSeconds() : 0;
            packet.PreferredLanguage = outcome.Language;
            packet.IsGuest = outcome.IsGuest;
            packet.RegistrationPromptUnlocked = outcome.RegistrationPromptUnlocked;
            packet.Nickname = outcome.Nickname;
            await sender.ReplyAsync(packet).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
