// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Domain.Admin;
using Nosum.Host.Ops;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The database side of the operations work against a real PostgreSQL server (docs/adr/ADR-031 D3, D4, D7): readiness, the migration lock and the
/// host's <c>admin</c> command.
/// </summary>
public sealed class OpsDatabaseTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _database;
    private readonly string _emptyName = "nosum_ops_" + Guid.NewGuid().ToString("N");

    public OpsDatabaseTests(PostgresFixture database) => _database = database;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using NpgsqlConnection poolKey = new(NosumDatabase.WithPoolLimits(this.EmptyConnection()));
        NpgsqlConnection.ClearPool(poolKey);
        await ExecuteAsync(this.AdminConnection(), "DROP DATABASE IF EXISTS \"" + _emptyName + "\" WITH (FORCE)");
    }

    [Fact]
    public async Task IsReady_MigratedDatabase_IsTrue()
        => (await NosumDatabase.IsReadyAsync(_database.Options, CancellationToken.None)).Should().BeTrue();

    [Fact]
    public async Task IsReady_DatabaseWithMigrationsStillPending_IsFalse()
    {
        await ExecuteAsync(this.AdminConnection(), "CREATE DATABASE \"" + _emptyName + "\"");

        (await NosumDatabase.IsReadyAsync(this.EmptyOptions(), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task IsReady_NoServerThere_IsFalseNotAnException()
    {
        DbContextOptions<NosumDbContext> options = NosumDatabase.CreateOptions("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        (await NosumDatabase.IsReadyAsync(options, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Migrate_TwoHostsStartingTogether_BothFinishAndTheSchemaIsCompleteOnce()
    {
        await ExecuteAsync(this.AdminConnection(), "CREATE DATABASE \"" + _emptyName + "\"");
        DbContextOptions<NosumDbContext> options = this.EmptyOptions();

        await Task.WhenAll(
            NosumDatabase.MigrateAsync(options, CancellationToken.None),
            NosumDatabase.MigrateAsync(options, CancellationToken.None),
            NosumDatabase.MigrateAsync(options, CancellationToken.None));

        (await NosumDatabase.IsReadyAsync(options, CancellationToken.None)).Should().BeTrue();
        await using NosumDbContext db = new(options);
        (await db.Database.GetAppliedMigrationsAsync()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Migrate_WhileAnotherHostHoldsTheAdvisoryLock_WaitsUntilItIsReleased()
    {
        await ExecuteAsync(this.AdminConnection(), "CREATE DATABASE \"" + _emptyName + "\"");
        DbContextOptions<NosumDbContext> options = this.EmptyOptions();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        await using NpgsqlConnection holder = new(this.EmptyConnection());
        await holder.OpenAsync(timeout.Token);
        await ExecuteAsync(holder, $"SELECT pg_advisory_lock({NosumDatabase.MigrationLockKey})");

        Task migrate = NosumDatabase.MigrateAsync(options, timeout.Token);

        // It is queued behind the lock, not running: wait until PostgreSQL shows the request as ungranted, then check nothing was migrated.
        await using NpgsqlConnection observer = new(this.EmptyConnection());
        await observer.OpenAsync(timeout.Token);
        while (await ScalarAsync(observer, "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted") == 0)
        {
            await Task.Delay(20, timeout.Token);
        }

        migrate.IsCompleted.Should().BeFalse();
        (await ScalarAsync(observer, "SELECT count(*) FROM information_schema.tables WHERE table_name = 'players'")).Should().Be(0);

        await ExecuteAsync(holder, $"SELECT pg_advisory_unlock({NosumDatabase.MigrationLockKey})");
        await migrate;
        (await NosumDatabase.IsReadyAsync(options, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task AdminGrant_ThenRevoke_ChangesTheRoleAndAuditsBoth()
    {
        string nickname = await CreateAsync();
        using StringWriter output = new();

        int granted = await HostCommands.RunAsync(["admin", "grant", nickname.ToUpperInvariant()], () => _database.Options, 0, output, CancellationToken.None);
        PlayerRole afterGrant = await RoleOfAsync(nickname);
        int listed = await HostCommands.RunAsync(["admin", "list"], () => _database.Options, 0, output, CancellationToken.None);
        int revoked = await HostCommands.RunAsync(["admin", "revoke", nickname], () => _database.Options, 0, output, CancellationToken.None);
        PlayerRole afterRevoke = await RoleOfAsync(nickname);

        granted.Should().Be(0);
        afterGrant.Should().Be(PlayerRole.Admin);
        listed.Should().Be(0);
        output.ToString().Should().Contain(nickname);
        revoked.Should().Be(0);
        afterRevoke.Should().Be(PlayerRole.Player);
        await using NosumDbContext db = new(_database.Options);
        Guid id = (await db.Players.SingleAsync(p => p.Nickname == nickname)).Id;
        (await db.AdminAudit.Where(a => a.TargetId == id).OrderBy(a => a.CreatedAt).Select(a => a.Action).ToListAsync())
            .Should().Equal((byte)AdminAction.GrantRole, (byte)AdminAction.RevokeRole);
    }

    [Fact]
    public async Task AdminGrant_UnknownNicknameOrGuest_FailsAndChangesNothing()
    {
        AccountService accounts = new AccountService(new EfUnitOfWorkFactory(_database.Options), FakeExternalTokenVerifier.All(), new SessionTokenService(), TimeProvider.System);
        Nosum.Abstractions.Accounts.AccountOutcome guest = await accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None);
        using StringWriter output = new();

        int unknown = await HostCommands.RunAsync(["admin", "grant", TestNicknames.Next()], () => _database.Options, 0, output, CancellationToken.None);
        int guestResult = await HostCommands.RunAsync(["admin", "grant", guest.Nickname], () => _database.Options, 0, output, CancellationToken.None);

        unknown.Should().Be(1);
        guestResult.Should().Be(1);
        (await RoleOfAsync(guest.Nickname)).Should().Be(PlayerRole.Player);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("admin", "promote", "somebody")]
    [InlineData("nonsense")]
    public async Task Command_WithBadArguments_PrintsUsageAndExitsTwo(params string[] args)
    {
        using StringWriter output = new();

        int code = await HostCommands.RunAsync(args, () => _database.Options, 0, output, CancellationToken.None);

        code.Should().Be(2);
        output.ToString().Should().Contain("usage:");
    }

    // ---- helpers ----

    private async Task<string> CreateAsync()
    {
        AccountService accounts = new AccountService(new EfUnitOfWorkFactory(_database.Options), FakeExternalTokenVerifier.All(), new SessionTokenService(), TimeProvider.System);
        Nosum.Abstractions.Accounts.AccountOutcome outcome = await TestAccounts.RegisterAsync(accounts, Guid.NewGuid().ToString("N"), TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return outcome.Nickname;
    }

    private async Task<PlayerRole> RoleOfAsync(string nickname)
    {
        await using NosumDbContext db = new(_database.Options);
        return (await db.Players.AsNoTracking().SingleAsync(p => p.Nickname == nickname)).Role;
    }

    private DbContextOptions<NosumDbContext> EmptyOptions() => NosumDatabase.CreateOptions(this.EmptyConnection());

    private string EmptyConnection() => new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = _emptyName }.ConnectionString;

    private string AdminConnection() => new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = "postgres" }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
