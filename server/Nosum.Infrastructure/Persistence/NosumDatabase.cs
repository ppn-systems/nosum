// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Nosum.Infrastructure.Persistence;

/// <summary>Database setup shared by the host, the design-time factory and tests.</summary>
public static class NosumDatabase
{
    /// <summary>Environment variable (ASP.NET Core configuration key form) that carries the connection string.</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__Default";

    /// <summary>Key of the advisory lock migrations hold (the ASCII bytes of <c>NOSUMMIG</c>).</summary>
    public const long MigrationLockKey = 0x4E4F53554D4D4947;

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

    /// <summary>
    /// Applies every pending migration while holding a PostgreSQL advisory lock (docs/adr/ADR-031 D3), so two hosts that start together never migrate
    /// at the same time: the second waits, then finds nothing left to apply. The lock lives on its own connection and is released when that closes.
    /// </summary>
    /// <param name="options">Options from <see cref="CreateOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>A task that completes when the schema is up to date.</returns>
    public static async Task MigrateAsync(DbContextOptions<NosumDbContext> options, CancellationToken cancellationToken)
    {
        NosumDbContext context = new(options);
        await using (context.ConfigureAwait(false))
        {
            NpgsqlConnection gate = new(context.Database.GetConnectionString());
            await using (gate.ConfigureAwait(false))
            {
                await gate.OpenAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteLockAsync(gate, "pg_advisory_lock", cancellationToken).ConfigureAwait(false);
                try
                {
                    await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await ExecuteLockAsync(gate, "pg_advisory_unlock", CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Tells whether the database answers and has no migration left to apply (what <c>/readyz</c> reports).</summary>
    /// <param name="options">Options from <see cref="CreateOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when the database is reachable and up to date; <see langword="false"/> otherwise, including on any error.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "A readiness probe answers yes or no; any failure means not ready.")]
    public static async Task<bool> IsReadyAsync(DbContextOptions<NosumDbContext> options, CancellationToken cancellationToken)
    {
        try
        {
            NosumDbContext context = new(options);
            await using (context.ConfigureAwait(false))
            {
                if (!await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                using IEnumerator<string> pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).GetEnumerator();
                return !pending.MoveNext();
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task ExecuteLockAsync(NpgsqlConnection connection, string function, CancellationToken cancellationToken)
    {
        NpgsqlCommand command = new("SELECT " + function + "(@key)", connection);
        await using (command.ConfigureAwait(false))
        {
            _ = command.Parameters.AddWithValue("key", MigrationLockKey);
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
