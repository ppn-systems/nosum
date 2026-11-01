// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The database connections a server keeps open stay within the pool cap and return to their baseline after connect and disconnect cycles
/// (docs/adr/ADR-087). Counted on the server, in <c>pg_stat_activity</c>, for this test's database only.
/// </summary>
public sealed class ConnectionPoolTests : IClassFixture<PostgresFixture>
{
    // More concurrent operations than the pool cap, so the pool has to open as many connections as it is allowed to.
    private const int BurstSize = 60;
    private const int Cycles = 100;

    private readonly PostgresFixture _database;

    public ConnectionPoolTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task ConcurrentBurst_LeavesNoMoreOpenConnectionsThanThePoolCap()
    {
        await RunBurstAsync(this._database);

        int open = await this.OpenConnectionsAsync();

        open.Should().BeLessThanOrEqualTo(NosumDatabase.MaxPoolSize, "the pool keeps at most MaxPoolSize connections to the database");
    }

    [Fact]
    public async Task ConnectDisconnectCycles_DoNotGrowTheOpenConnections()
    {
        await RunBurstAsync(this._database);
        int baseline = await this.OpenConnectionsAsync();

        for (int i = 0; i < Cycles; i++)
        {
            await using NosumDbContext context = new(this._database.Options);
            _ = await context.Database.ExecuteSqlRawAsync("SELECT 1");
        }

        int after = await this.OpenConnectionsAsync();
        after.Should().BeLessThanOrEqualTo(baseline, "a connect and disconnect cycle reuses a pooled connection and opens no new one");
    }

    private static Task RunBurstAsync(PostgresFixture database)
        => Task.WhenAll(Enumerable.Range(0, BurstSize).Select(_ => QueryAsync(database)));

    private static async Task QueryAsync(PostgresFixture database)
    {
        await using NosumDbContext context = new(database.Options);
        _ = await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.2)");
    }

    // Client backends of this database, not counting the connection that asks. That connection is unpooled, so it is not a pool connection.
    private async Task<int> OpenConnectionsAsync()
    {
        NpgsqlConnectionStringBuilder builder = new(this._database.ConnectionString) { Pooling = false };
        await using NpgsqlConnection counter = new(builder.ConnectionString);
        await counter.OpenAsync();
        await using NpgsqlCommand command = new(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND backend_type = 'client backend'", counter);
        long total = (long)(await command.ExecuteScalarAsync())!;
        return (int)total - 1;
    }
}
