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
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Account;

/// <summary>
/// Frames that carry an identity token or a session token are refused unless they are encrypted (<see cref="PacketEncryptionAttribute"/>).
/// Google or Microsoft sign-in, guest, resume, identity link, logout and language preference (docs/adr/ADR-069). Sign-in, guest and resume are public;
/// the rest need a signed-in player.
/// </summary>
[PacketHandler("Nosum.Account")]
public static partial class AccountHandlers
{
    [Inject] private static IAccountService s_accounts = null!;
    [Inject] private static ISessionRegistry s_sessions = null!;
    [Inject] private static ILookService s_looks = null!;
    [Inject] private static IAbuseGuard s_abuse = null!;

    /// <summary>New guest accounts one address may create per day; more are refused with <see cref="AccountStatus.TooManyRequests"/> (docs/adr/ADR-032 D6).</summary>
    public static int NewAccountsPerAddressPerDay { get; set; } = 30;

    /// <summary>Sign-in attempts one address may make per minute (each one costs a key check); more are refused with <see cref="AccountStatus.TooManyRequests"/>.</summary>
    public const int SignInsPerAddressPerMinute = 15;

    /// <summary>Signs the connection in with a Google or Microsoft ID token, creating the account on first use.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.ExternalSignIn)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleExternalSignInAsync(IPacketContext<ExternalSignIn> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!s_abuse.TryAcquire("sign-in-attempt", AddressOf(context.Connection), SignInsPerAddressPerMinute, TimeSpan.FromMinutes(1)))
        {
            await ReplyOutcomeAsync(context.Sender, AccountOutcome.Refused(AccountStatus.TooManyRequests)).ConfigureAwait(false);
            return;
        }

        AccountOutcome outcome = await s_accounts
            .SignInWithExternalAsync(context.Packet.Provider, context.Packet.IdToken, context.Packet.PreferredLanguage, context.CancellationToken).ConfigureAwait(false);
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

    /// <summary>Turns the signed-in guest into a registered player by linking a Google or Microsoft identity, in place.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.LinkExternalIdentity)]
    [PacketEncryption(true)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleLinkExternalIdentityAsync(IPacketContext<LinkExternalIdentity> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayerIdentity player = context.Connection.GetPlayer();

        AccountOutcome outcome = await s_accounts
            .LinkExternalIdentityAsync(player.PlayerId, player.SessionId, context.Packet.Provider, context.Packet.IdToken, context.Packet.Nickname, context.CancellationToken)
            .ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
        if (outcome.Status == AccountStatus.Ok)
        {
            // The guest chose a nickname by linking: everyone on the map learns it through the look row, like after a rename.
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

    // The address limits are keyed by (a loopback address is exempt in the guard); an unreadable endpoint shares one key rather than skipping the limit.
    private static string AddressOf(IConnection connection) => connection.NetworkEndpoint?.Address ?? "unknown";

    // Signs the connection in on success (identity from the server's own outcome, never from the packet), then replies.
    private static async ValueTask CompleteAsync(IConnection connection, IPacketSender sender, AccountOutcome outcome)
    {
        if (outcome.Status == AccountStatus.Ok)
        {
            // One active connection per player: an older one is saved, told and closed before this one goes on.
            await s_sessions.ActivateAsync(outcome.PlayerId, connection).ConfigureAwait(false);
            connection.AttachPlayer(new PlayerIdentity(outcome.PlayerId, outcome.SessionId), outcome.IsAdmin);
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
