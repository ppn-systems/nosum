// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.IntegrationTests.Server.DbBudget;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The player facts of docs/adr/ADR-035 D5: the account behind a typed nickname and the guest flag are read once, and every change that ends in <c>Remember</c> is seen at once.</summary>
public sealed class PlayerFactsTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly DbCommandCounter _counter = new();
    private readonly EfUnitOfWorkFactory _units;
    private readonly NicknameDirectory _directory;

    public PlayerFactsTests(PostgresFixture database)
    {
        _database = database;
        DbContextOptions<NosumDbContext> options = new DbContextOptionsBuilder<NosumDbContext>().UseNpgsql(database.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(_counter).Options;
        _units = new EfUnitOfWorkFactory(options);
        _directory = new NicknameDirectory(_units);
    }

    [Fact]
    public async Task FindByKeyAsync_AskedAgain_CostsNoCommand()
    {
        (Guid id, string nickname) = await CreatePlayerAsync(guest: false);
        PlayerFact? first = await _directory.FindByKeyAsync(NicknameRules.Key(nickname));

        int mark = _counter.Mark;
        PlayerFact? again = await _directory.FindByKeyAsync(NicknameRules.Key(nickname));
        PlayerFact? byId = await _directory.FindAsync(id);

        _counter.Since(mark).Commands.Should().Be(1, "only the lookup by id, which had not been read yet");
        first.Should().Be(again);
        first!.Id.Should().Be(id);
        byId.Should().Be(first);
    }

    [Fact]
    public async Task FindByKeyAsync_NoSuchPlayer_IsNotKeptSoALaterRegistrationIsFound()
    {
        string nickname = "late" + Guid.NewGuid().ToString("N")[..8];
        (await _directory.FindByKeyAsync(NicknameRules.Key(nickname))).Should().BeNull();

        Guid id = await CreatePlayerNamedAsync(nickname, guest: false);
        _directory.Remember(id, nickname);

        (await _directory.FindByKeyAsync(NicknameRules.Key(nickname)))!.Id.Should().Be(id);
    }

    [Fact]
    public async Task Remember_AfterANicknameChange_ResolvesTheNewKeyAndForgetsTheOldOne()
    {
        (Guid id, string oldName) = await CreatePlayerAsync(guest: false);
        _ = await _directory.FindByKeyAsync(NicknameRules.Key(oldName));
        string newName = "new" + Guid.NewGuid().ToString("N")[..8];
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.Players.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Nickname, newName).SetProperty(p => p.NormalizedNickname, NicknameRules.Key(newName)));
        }

        _directory.Remember(id, newName);

        (await _directory.FindByKeyAsync(NicknameRules.Key(oldName))).Should().BeNull();
        (await _directory.FindByKeyAsync(NicknameRules.Key(newName)))!.Id.Should().Be(id);
        (await _directory.FindAsync(id))!.Nickname.Should().Be(newName);
    }

    [Fact]
    public async Task Remember_AfterAGuestRegisters_ShowsThemAsNoLongerAGuest()
    {
        (Guid id, string nickname) = await CreatePlayerAsync(guest: true);
        (await _directory.FindAsync(id))!.IsGuest.Should().BeTrue();
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.Players.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsGuest, false));
        }

        _directory.Remember(id, nickname);

        (await _directory.FindAsync(id))!.IsGuest.Should().BeFalse();
        (await _directory.FindByKeyAsync(NicknameRules.Key(nickname)))!.IsGuest.Should().BeFalse();
    }

    [Fact]
    public async Task FindAsync_UncachedDirectory_ReadsEveryTime()
    {
        (Guid id, _) = await CreatePlayerAsync(guest: false);
        NicknameDirectory uncached = new(_units, cacheFacts: false);
        _ = await uncached.FindAsync(id);

        int mark = _counter.Mark;
        _ = await uncached.FindAsync(id);

        _counter.Since(mark).Reads.Should().Be(1);
    }

    private async Task<(Guid Id, string Nickname)> CreatePlayerAsync(bool guest)
    {
        string nickname = "fact" + Guid.NewGuid().ToString("N")[..8];
        return (await CreatePlayerNamedAsync(nickname, guest), nickname);
    }

    private async Task<Guid> CreatePlayerNamedAsync(string nickname, bool guest)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = new() { IsGuest = guest, Nickname = nickname, NormalizedNickname = NicknameRules.Key(nickname), CreatedAt = s_start, LastActiveAt = s_start };
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }
}
