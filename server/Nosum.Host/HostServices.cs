// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Email;
using Nosum.Abstractions.Security;
using Nosum.Handlers.Account;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Email;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;

namespace Nosum.Host;

/// <summary>
/// Creates the server's services and registers them where Nalix handlers find them (<c>[Inject]</c>).
/// The one place that knows the Infrastructure implementations.
/// </summary>
internal static class HostServices
{
    /// <summary>Registers every service handlers inject.</summary>
    /// <param name="database">Options for the migrated database.</param>
    internal static void Register(DbContextOptions<NosumDbContext> database)
    {
        ArgumentNullException.ThrowIfNull(database);

        IUnitOfWorkFactory unitOfWork = new EfUnitOfWorkFactory(database);
        IPasswordHasher hasher = new Pbkdf2PasswordHasher();
        ISessionTokenService tokens = new SessionTokenService();

        InstanceManager instances = InstanceManager.Instance;
        instances.Register(unitOfWork);
        instances.Register(hasher);
        instances.Register(tokens);
        NicknameDirectory nicknames = new(unitOfWork);
        instances.Register<INicknameDirectory>(nicknames);

        // Account recovery (docs/adr/ADR-032 D4): SMTP when NOSUM_SMTP_HOST is set, otherwise a sender that sends nothing and logs only that a mail would have gone out.
        SmtpEmailOptions? smtp = SmtpEmailOptions.FromEnvironment();
        IEmailSender email = smtp is null ? new LoggingEmailSender() : new SmtpEmailSender(smtp);
        if (smtp is null)
        {
            Console.Error.WriteLine("NOSUM_SMTP_HOST is not set: password recovery codes are not delivered.");
        }

        instances.Register(email);
        AccountHandlers.NewAccountsPerAddressPerDay = AbuseLimits.NewAccountsPerAddressPerDay();
        instances.Register<IAbuseGuard>(new SlidingWindowAbuseGuard(TimeProvider.System, exemptLoopback: !AbuseLimits.EnforceLoopback()));
        instances.Register<IAccountService>(new AccountService(unitOfWork, hasher, tokens, TimeProvider.System, nicknames: nicknames, email: email));
        PlayerLifecycleService lifecycle = new(unitOfWork, TimeProvider.System);
        instances.Register<IPlayerActivityService>(lifecycle);
        instances.Register<IRegistrationPromptService>(lifecycle);
        // The row of a player's saved place, energy and experience is held once and shared by the two services that write it (docs/adr/ADR-035 D4).
        PlayerStateCache playerStates = new();
        instances.Register(playerStates);
        instances.Register<IPlayerStateService>(new PlayerStateService(unitOfWork, TimeProvider.System, playerStates));
    }
}
