// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Nicknames against a real PostgreSQL database: uniqueness (including a race), guest names, rename cooldown, the cache (docs/adr/ADR-009 amendments).</summary>
public sealed class NicknameServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static int s_email;

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly AccountService _accounts;
    private readonly NicknameDirectory _directory;

    public NicknameServiceTests(PostgresFixture database)
    {
        _database = database;
        EfUnitOfWorkFactory units = new(database.Options);
        _directory = new NicknameDirectory(units);
        _accounts = new AccountService(units, new Pbkdf2PasswordHasher(iterations: 1000), new SessionTokenService(), _clock, nicknames: _directory);
    }

    [Fact]
    public async Task Register_VietnameseNickname_IsStoredComposedAndReturned()
    {
        string decomposed = "Nguyễn Văn_An".Normalize(NormalizationForm.FormD);

        AccountOutcome outcome = await RegisterAsync(decomposed);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.Nickname.Should().Be("Nguyễn Văn_An".Normalize(NormalizationForm.FormC));
        (await _directory.GetAsync(outcome.PlayerId)).Should().Be(outcome.Nickname);
        (await _accounts.ResumeAsync(outcome.SessionToken, CancellationToken.None)).Nickname.Should().Be(outcome.Nickname);
    }

    [Theory]
    [InlineData("ab", AccountStatus.InvalidNickname)]
    [InlineData("bad-name", AccountStatus.InvalidNickname)]
    [InlineData("", AccountStatus.InvalidNickname)]
    [InlineData("Admin", AccountStatus.NicknameReserved)]
    [InlineData("Quản Trị", AccountStatus.NicknameReserved)]
    public async Task Register_InvalidOrReserved_IsRefusedWithMatchingCodeAndCreatesNothing(string nickname, AccountStatus expected)
    {
        string email = NextEmail();

        AccountOutcome outcome = await _accounts.RegisterAsync(email, "password-1", nickname, GameLanguage.Unspecified, CancellationToken.None);

        outcome.Status.Should().Be(expected);
        await using NosumDbContext context = new(_database.Options);
        (await context.Players.AnyAsync(p => p.NormalizedEmail == email)).Should().BeFalse();
    }

    [Fact]
    public async Task Register_SameNicknameDifferentCaseAccentsComposition_IsTaken_ButADifferentAccentIsADifferentName()
    {
        string name = "Tên" + Guid.NewGuid().ToString("N")[..6];
        (await RegisterAsync(name)).Status.Should().Be(AccountStatus.Ok);

        (await RegisterAsync(name.ToUpperInvariant())).Status.Should().Be(AccountStatus.NicknameTaken);
        (await RegisterAsync(name.Normalize(NormalizationForm.FormD))).Status.Should().Be(AccountStatus.NicknameTaken);
        (await RegisterAsync(name.Replace('ê', 'e'))).Status.Should().Be(AccountStatus.Ok, "Ten and Tên are different names");
    }

    [Fact]
    public async Task Register_SameNicknameConcurrently_CreatesExactlyOne_AndTheLoserSeesNicknameTaken()
    {
        string name = "Race" + Guid.NewGuid().ToString("N")[..8];

        AccountOutcome[] results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RegisterAsync(name)));

        results.Count(r => r.Status == AccountStatus.Ok).Should().Be(1);
        results.Where(r => r.Status != AccountStatus.Ok).Should().OnlyContain(r => r.Status == AccountStatus.NicknameTaken);
        await using NosumDbContext context = new(_database.Options);
        (await context.Players.CountAsync(p => p.NormalizedNickname == NicknameRules.Key(name))).Should().Be(1);
    }

    [Fact]
    public async Task Register_SameEmailAndNickname_ReportsTheEmailWhenOnlyTheEmailClashes()
    {
        string email = NextEmail();
        (await _accounts.RegisterAsync(email, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);

        AccountOutcome again = await _accounts.RegisterAsync(email, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        again.Status.Should().Be(AccountStatus.EmailTaken);
    }

    [Fact]
    public async Task StartGuest_GetsNeutralUniqueNames()
    {
        AccountOutcome[] guests = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None)));

        guests.Should().OnlyContain(g => g.Status == AccountStatus.Ok);
        guests.Select(g => g.Nickname).Should().OnlyHaveUniqueItems().And.OnlyContain(n => IsGuest(n));
        NicknameRules.Validate(guests[0].Nickname).Should().Be(NicknameVerdict.Invalid, "a player cannot type a guest name");
    }

    [Fact]
    public async Task UpgradeGuest_TakesTheChosenNickname_AndRefusesATakenOneLeavingTheGuest()
    {
        string taken = TestNicknames.Next();
        (await _accounts.RegisterAsync(NextEmail(), "password-1", taken, GameLanguage.Unspecified, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome refused = await _accounts.UpgradeGuestAsync(guest.PlayerId, guest.SessionId, NextEmail(), "password-1", taken.ToLowerInvariant(), CancellationToken.None);
        refused.Status.Should().Be(AccountStatus.NicknameTaken);
        (await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None)).Nickname.Should().Be(guest.Nickname, "still a guest with the same name");

        string chosen = TestNicknames.Next();
        AccountOutcome upgraded = await _accounts.UpgradeGuestAsync(guest.PlayerId, guest.SessionId, NextEmail(), "password-1", chosen, CancellationToken.None);

        upgraded.Status.Should().Be(AccountStatus.Ok);
        upgraded.Nickname.Should().Be(chosen);
        (await _directory.GetAsync(guest.PlayerId)).Should().Be(chosen, "the cache follows the change");
    }

    [Fact]
    public async Task SetNickname_FirstChangeIsFree_ThenTheSecondWaitsTwentyFourHours()
    {
        AccountOutcome player = await RegisterAsync(TestNicknames.Next());
        string first = TestNicknames.Next();
        string second = TestNicknames.Next();

        NicknameOutcome changed = await _accounts.SetNicknameAsync(player.PlayerId, first, CancellationToken.None);
        changed.Status.Should().Be(AccountStatus.Ok);
        changed.Nickname.Should().Be(first);
        changed.NextChangeAt.Should().Be(s_start.AddHours(24));
        (await _directory.GetAsync(player.PlayerId)).Should().Be(first);

        _clock.Advance(TimeSpan.FromHours(23));
        NicknameOutcome early = await _accounts.SetNicknameAsync(player.PlayerId, second, CancellationToken.None);
        early.Status.Should().Be(AccountStatus.NicknameTooSoon);
        early.Nickname.Should().Be(first);
        early.NextChangeAt.Should().Be(s_start.AddHours(24));

        _clock.Advance(TimeSpan.FromHours(1.01));
        (await _accounts.SetNicknameAsync(player.PlayerId, second, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
        (await _accounts.ResumeAsync(player.SessionToken, CancellationToken.None)).Nickname.Should().Be(second);
    }

    [Fact]
    public async Task SetNickname_TakenInvalidReservedOrUnchanged_DoesNotStartTheWait()
    {
        AccountOutcome a = await RegisterAsync(TestNicknames.Next());
        AccountOutcome b = await RegisterAsync(TestNicknames.Next());

        (await _accounts.SetNicknameAsync(a.PlayerId, b.Nickname.ToUpperInvariant(), CancellationToken.None)).Status.Should().Be(AccountStatus.NicknameTaken);
        (await _accounts.SetNicknameAsync(a.PlayerId, "x", CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidNickname);
        (await _accounts.SetNicknameAsync(a.PlayerId, "Moderator", CancellationToken.None)).Status.Should().Be(AccountStatus.NicknameReserved);
        (await _accounts.SetNicknameAsync(a.PlayerId, a.Nickname, CancellationToken.None)).Should().Match<NicknameOutcome>(o => o.Status == AccountStatus.Ok && o.NextChangeAt == null);

        // Changing only the case of one's own name is a change, and allowed (it is not "taken" by oneself).
        (await _accounts.SetNicknameAsync(a.PlayerId, a.Nickname.ToUpperInvariant(), CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task SetNickname_TwoPlayersWantTheSameNameAtOnce_ExactlyOneGetsIt()
    {
        AccountOutcome a = await RegisterAsync(TestNicknames.Next());
        AccountOutcome b = await RegisterAsync(TestNicknames.Next());
        string wanted = "Want" + Guid.NewGuid().ToString("N")[..8];

        NicknameOutcome[] results = await Task.WhenAll(
            _accounts.SetNicknameAsync(a.PlayerId, wanted, CancellationToken.None),
            _accounts.SetNicknameAsync(b.PlayerId, wanted, CancellationToken.None));

        results.Count(r => r.Status == AccountStatus.Ok).Should().Be(1);
        results.Single(r => r.Status != AccountStatus.Ok).Status.Should().Be(AccountStatus.NicknameTaken);
    }

    [Fact]
    public async Task SetNickname_TwoRenamesOfOnePlayerAtOnce_OnlyOneIsAllowedInTheWindow()
    {
        AccountOutcome player = await RegisterAsync(TestNicknames.Next());

        NicknameOutcome[] results = await Task.WhenAll(
            _accounts.SetNicknameAsync(player.PlayerId, TestNicknames.Next(), CancellationToken.None),
            _accounts.SetNicknameAsync(player.PlayerId, TestNicknames.Next(), CancellationToken.None),
            _accounts.SetNicknameAsync(player.PlayerId, TestNicknames.Next(), CancellationToken.None));

        results.Count(r => r.Status == AccountStatus.Ok).Should().Be(1);
        results.Where(r => r.Status != AccountStatus.Ok).Should().OnlyContain(r => r.Status == AccountStatus.NicknameTooSoon);
    }

    [Fact]
    public async Task Migration_BackfillsDistinctGuestNamesForPlayersThatExistedBeforeNicknames()
    {
        // A scratch database at the schema before nicknames, with two players in it, then migrated forward.
        string scratch = "nosum_nick_" + Guid.NewGuid().ToString("N");
        NpgsqlConnectionStringBuilder admin = new(_database.ConnectionString) { Database = "postgres", Pooling = false };
        await using (NpgsqlConnection connection = new(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand create = new("CREATE DATABASE \"" + scratch + "\"", connection);
            _ = await create.ExecuteNonQueryAsync();
        }

        try
        {
            NpgsqlConnectionStringBuilder target = new(_database.ConnectionString) { Database = scratch, Pooling = false };
            DbContextOptions<NosumDbContext> options = NosumDatabase.CreateOptions(target.ConnectionString);
            await using (NosumDbContext before = new(options))
            {
                await before.GetService<IMigrator>().MigrateAsync("20261025080342_SocialLayer");
                _ = await before.Database.ExecuteSqlRawAsync(
                    "INSERT INTO players (id, is_guest, language, created_at, last_active_at) VALUES " +
                    "('0197a4c0-0000-7000-8000-000000000001', true, 1, '2026-01-01', '2026-01-01'), " +
                    "('0197a4c0-0000-7000-8000-000000000002', false, 1, '2026-01-02', '2026-01-02')");
            }

            await NosumDatabase.MigrateAsync(options, CancellationToken.None);

            await using NosumDbContext after = new(options);
            Player[] players = await after.Players.OrderBy(p => p.CreatedAt).ToArrayAsync();
            players.Select(p => p.Nickname).Should().Equal("guest-0001", "guest-0002");
            players.Select(p => p.NormalizedNickname).Should().Equal("guest-0001", "guest-0002");
            players.Should().OnlyContain(p => p.NicknameChangedAt == null, "they may rename at once");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using NpgsqlConnection connection = new(admin.ConnectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand drop = new("DROP DATABASE IF EXISTS \"" + scratch + "\" WITH (FORCE)", connection);
            _ = await drop.ExecuteNonQueryAsync();
        }
    }

    private static bool IsGuest(string nickname) => NicknameRules.IsGuestName(nickname, out string _);

    private Task<AccountOutcome> RegisterAsync(string nickname)
        => _accounts.RegisterAsync(NextEmail(), "password-1", nickname, GameLanguage.Unspecified, CancellationToken.None);

    private static string NextEmail() => "nick" + Interlocked.Increment(ref s_email) + Guid.NewGuid().ToString("N")[..8] + "@gmail.com";
}
