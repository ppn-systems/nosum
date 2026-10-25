// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Nosum.Infrastructure.Persistence;

/// <summary>Database setup shared by the host, the design-time factory and tests.</summary>
public static class NosumDatabase
{
    /// <summary>Environment variable (ASP.NET Core configuration key form) that carries the connection string.</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__Default";

    /// <summary>Connection string of the local development database. Never valid in Production.</summary>
    public const string DevelopmentConnectionString =
        "Host=localhost;Port=5432;Database=nosum_dev;Username=nosum;Password=dev_password_not_for_production";

    /// <summary>
    /// Picks the connection string: the environment variable when set, otherwise the development
    /// database - except in Production, where a missing variable is a startup error.
    /// </summary>
    /// <param name="fromEnvironment">Value of <see cref="ConnectionStringVariable"/>, or null.</param>
    /// <param name="isProduction">Whether the process runs in the Production environment.</param>
    /// <returns>The connection string to use.</returns>
    /// <exception cref="InvalidOperationException">Production without a connection string.</exception>
    public static string ResolveConnectionString(string? fromEnvironment, bool isProduction)
    {
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        if (isProduction)
        {
            throw new InvalidOperationException(
                ConnectionStringVariable + " is not set. Refusing to start in Production: the server must not fall back to the local development database.");
        }

        return DevelopmentConnectionString;
    }

    /// <summary>Builds the EF options: PostgreSQL with snake_case names.</summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>Options for <see cref="NosumDbContext"/>.</returns>
    public static DbContextOptions<NosumDbContext> CreateOptions(string connectionString)
        => new DbContextOptionsBuilder<NosumDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

    /// <summary>Applies every pending migration.</summary>
    /// <param name="options">Options from <see cref="CreateOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>A task that completes when the schema is up to date.</returns>
    public static async Task MigrateAsync(DbContextOptions<NosumDbContext> options, CancellationToken cancellationToken)
    {
        NosumDbContext context = new(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
