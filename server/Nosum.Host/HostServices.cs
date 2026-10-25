// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Security;
using Nosum.Infrastructure.Accounts;
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
        instances.Register<IAccountService>(new AccountService(unitOfWork, hasher, tokens, TimeProvider.System));
        PlayerLifecycleService lifecycle = new(unitOfWork, TimeProvider.System);
        instances.Register<IPlayerActivityService>(lifecycle);
        instances.Register<IRegistrationPromptService>(lifecycle);
        instances.Register<IPlayerStateService>(new PlayerStateService(unitOfWork, TimeProvider.System));
    }
}
