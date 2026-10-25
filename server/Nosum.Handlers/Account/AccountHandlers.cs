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
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Account;

/// <summary>Register, login, resume and language preference. Register, login and resume are public; the rest need a signed-in player.</summary>
[PacketHandler("Nosum.Account")]
public static partial class AccountHandlers
{
    [Inject] private static IAccountService s_accounts = null!;

    /// <summary>Creates an account and signs the connection in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.Register)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleRegisterAsync(IPacketContext<Register> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string username = context.Packet.Username;
        string password = context.Packet.Password;
        string language = context.Packet.PreferredLanguage;

        AccountOutcome outcome = await s_accounts.RegisterAsync(username, password, language, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
    }

    /// <summary>Signs the connection in with a username and password.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.Login)]
    [PacketPermission(PermissionLevel.NONE)]
    [PacketRateLimit(1, 5, PolicyId = "account-credential", HardLockoutSeconds = 60, MaxSoftViolations = 10)]
    public static async ValueTask HandleLoginAsync(IPacketContext<Login> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string username = context.Packet.Username;
        string password = context.Packet.Password;

        AccountOutcome outcome = await s_accounts.LoginAsync(username, password, context.CancellationToken).ConfigureAwait(false);
        await CompleteAsync(context.Connection, context.Sender, outcome).ConfigureAwait(false);
    }

    /// <summary>Signs the connection in again with a session token.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply is sent.</returns>
    [PacketOpcode(AccountOpCode.ResumeSession)]
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
        string language = context.Packet.Language;

        AccountStatus status = await s_accounts.SetLanguageAsync(player.PlayerId, language, context.CancellationToken).ConfigureAwait(false);

        PacketScope<LanguageResult> lease = PacketFactory<LanguageResult>.Acquire();
        try
        {
            lease.Value.Status = status;
            lease.Value.Language = status == AccountStatus.Ok ? language : string.Empty;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
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
            connection.AttachPlayer(new PlayerIdentity(outcome.PlayerId, outcome.Username));
        }

        PacketScope<AccountResult> lease = PacketFactory<AccountResult>.Acquire();
        try
        {
            AccountResult packet = lease.Value;
            packet.Status = outcome.Status;
            packet.PlayerId = outcome.PlayerId;
            packet.Username = outcome.Username;
            packet.SessionToken = outcome.SessionToken;
            packet.ExpiresAtUnixSeconds = outcome.Status == AccountStatus.Ok ? outcome.ExpiresAt.ToUnixTimeSeconds() : 0;
            packet.PreferredLanguage = outcome.PreferredLanguage;
            await sender.ReplyAsync(packet).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
