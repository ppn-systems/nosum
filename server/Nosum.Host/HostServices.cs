// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Identity;
using Nosum.Abstractions.Security;
using Nosum.Handlers.Account;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Identity;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;

namespace Nosum.Host;

/// <summary>
/// Creates the server's services and registers them where Nalix handlers find them (<c>[Inject]</c>).
/// The one place that knows the Infrastructure implementations.
/// </summary>
internal static class HostServices
{
    // Lives as long as the process: the provider key sets are fetched through it (docs/adr/ADR-069 D3).
    private static readonly HttpClient s_keys = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Registers every service handlers inject.</summary>
    /// <param name="database">Options for the migrated database.</param>
    internal static void Register(DbContextOptions<NosumDbContext> database)
    {
        ArgumentNullException.ThrowIfNull(database);

        IUnitOfWorkFactory unitOfWork = new EfUnitOfWorkFactory(database);
        ISessionTokenService tokens = new SessionTokenService();

        // Google and Microsoft keys are fetched with one client that gives up after a few seconds (docs/adr/ADR-069 D3).
        ExternalSignInOptions external = ExternalSignInOptions.FromEnvironment();
        external.Validate();
        if (external.GoogleClientId.Length == 0)
        {
            Console.Error.WriteLine("Google sign-in is off: NOSUM_GOOGLE_CLIENT_ID is not set.");
        }

        if (external.MicrosoftClientId.Length == 0)
        {
            Console.Error.WriteLine("Microsoft sign-in is off: NOSUM_MICROSOFT_CLIENT_ID is not set.");
        }

        IExternalTokenVerifier[] verifiers =
        [
            new GoogleTokenVerifier(external, s_keys, TimeProvider.System),
            new MicrosoftTokenVerifier(external, s_keys, TimeProvider.System),
        ];

        InstanceManager instances = InstanceManager.Instance;
        instances.Register(unitOfWork);
        instances.Register(tokens);
        NicknameDirectory nicknames = new(unitOfWork);
        instances.Register<INicknameDirectory>(nicknames);

        AccountHandlers.NewAccountsPerAddressPerDay = AbuseLimits.NewAccountsPerAddressPerDay();
        instances.Register<IAbuseGuard>(new SlidingWindowAbuseGuard(TimeProvider.System, exemptLoopback: !AbuseLimits.EnforceLoopback()));
        instances.Register<IAccountService>(new AccountService(unitOfWork, verifiers, tokens, TimeProvider.System, nicknames: nicknames));
        PlayerLifecycleService lifecycle = new(unitOfWork, TimeProvider.System);
        instances.Register<IPlayerActivityService>(lifecycle);
        instances.Register<IRegistrationPromptService>(lifecycle);
        // The row of a player's saved place, energy and experience is held once and shared by the two services that write it (docs/adr/ADR-035 D4).
        PlayerStateCache playerStates = new();
        instances.Register(playerStates);
        instances.Register<IPlayerStateService>(new PlayerStateService(unitOfWork, TimeProvider.System, playerStates));
    }
}
