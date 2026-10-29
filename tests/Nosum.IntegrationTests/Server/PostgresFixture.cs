// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// A throw-away database on a real PostgreSQL server, migrated with the shipped migrations.
/// The server comes from <c>NOSUM_TEST_POSTGRES</c> (an admin connection string that may create databases);
/// it defaults to the local development server. A missing server fails the tests, it does not skip them.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultAdminConnection =
        "Host=localhost;Port=5432;Database=postgres;Username=nosum;Password=dev_password_not_for_production";

    private readonly string _databaseName = "nosum_test_" + Guid.NewGuid().ToString("N");
    private string _adminConnection = DefaultAdminConnection;

    /// <summary>Gets the options of the migrated test database.</summary>
    public DbContextOptions<NosumDbContext> Options { get; private set; } = null!;

    /// <summary>Gets the connection string of the migrated test database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _adminConnection = Environment.GetEnvironmentVariable("NOSUM_TEST_POSTGRES") ?? DefaultAdminConnection;
        await ExecuteAdminAsync("CREATE DATABASE \"" + _databaseName + "\"");

        NpgsqlConnectionStringBuilder builder = new(_adminConnection) { Database = _databaseName };
        this.ConnectionString = builder.ConnectionString;
        this.Options = NosumDatabase.CreateOptions(this.ConnectionString);
        await NosumDatabase.MigrateAsync(this.Options, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        // An autovacuum worker is a superuser backend: while it works in a fresh database, WITH (FORCE) is refused with 42501 (insufficient privilege)
        // and the drop succeeds once the worker has finished. Only that refusal is retried; any other error fails the teardown at once.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteAdminAsync("DROP DATABASE IF EXISTS \"" + _databaseName + "\" WITH (FORCE)");
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "42501" && attempt < 40)
            {
                await Task.Delay(250);
            }
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using NpgsqlConnection connection = new(_adminConnection);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        _ = await command.ExecuteNonQueryAsync();
    }
}
