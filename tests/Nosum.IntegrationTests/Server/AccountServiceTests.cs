// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Account services against a real PostgreSQL database.</summary>
public sealed class AccountServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly AccountService _accounts;

    public AccountServiceTests(PostgresFixture database)
    {
        _database = database;
        _accounts = new AccountService(
            new EfUnitOfWorkFactory(database.Options),
            new Pbkdf2PasswordHasher(iterations: 1000),
            new SessionTokenService(),
            _clock);
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_CreatesPlayerWithSessionAndDefaultLanguage()
    {
        string name = Unique();

        AccountOutcome outcome = await _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.PlayerId.Should().NotBeEmpty();
        outcome.Username.Should().Be(name);
        outcome.SessionToken.Should().NotBeEmpty();
        outcome.ExpiresAt.Should().Be(s_start + AccountService.DefaultSessionLifetime);
        outcome.PreferredLanguage.Should().Be("vi");
    }

    [Fact]
    public async Task RegisterAsync_Stored_KeepsOnlyHashesNeverPasswordOrToken()
    {
        string name = Unique();

        AccountOutcome outcome = await _accounts.RegisterAsync(name, "password-1", "en", CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        Player player = await db.Players.SingleAsync(p => p.Id == outcome.PlayerId);
        player.PasswordHash.Should().StartWith("PBKDF2-SHA256$").And.NotContain("password-1");
        player.PreferredLanguage.Should().Be("en");
        PlayerSessionToken stored = await db.PlayerSessionTokens.SingleAsync(t => t.PlayerId == outcome.PlayerId);
        stored.TokenHash.Should().NotBe(outcome.SessionToken).And.NotContain(outcome.SessionToken);
    }

    [Fact]
    public async Task RegisterAsync_SameUsernameDifferentCase_IsUsernameTaken()
    {
        string name = Unique();
        _ = await _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None);

        AccountOutcome second = await _accounts.RegisterAsync(name.ToUpperInvariant(), "password-2", string.Empty, CancellationToken.None);

        second.Status.Should().Be(AccountStatus.UsernameTaken);
        second.SessionToken.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterAsync_SameUsernameConcurrently_CreatesExactlyOne()
    {
        string name = Unique();

        AccountOutcome[] results = await Task.WhenAll(
            _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None),
            _accounts.RegisterAsync(name, "password-2", string.Empty, CancellationToken.None),
            _accounts.RegisterAsync(name, "password-3", string.Empty, CancellationToken.None));

        results.Should().ContainSingle(r => r.Status == AccountStatus.Ok);
        results.Should().OnlyContain(r => r.Status == AccountStatus.Ok || r.Status == AccountStatus.UsernameTaken);
    }

    [Theory]
    [InlineData("ab", "password-1", "vi", AccountStatus.InvalidUsername)]
    [InlineData("1abc", "password-1", "vi", AccountStatus.InvalidUsername)]
    [InlineData("validname", "short", "vi", AccountStatus.InvalidPassword)]
    [InlineData("validname", "password-1", "not a tag", AccountStatus.InvalidLanguage)]
    public async Task RegisterAsync_BreaksARule_IsRefusedWithMatchingCode(string name, string password, string language, AccountStatus expected)
    {
        AccountOutcome outcome = await _accounts.RegisterAsync(name, password, language, CancellationToken.None);

        outcome.Status.Should().Be(expected);
        outcome.SessionToken.Should().BeEmpty();
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_IssuesNewTokenAndRecordsLastLogin()
    {
        string name = Unique();
        AccountOutcome registered = await _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));

        AccountOutcome login = await _accounts.LoginAsync(name.ToUpperInvariant(), "password-1", CancellationToken.None);

        login.Status.Should().Be(AccountStatus.Ok);
        login.PlayerId.Should().Be(registered.PlayerId);
        login.SessionToken.Should().NotBe(registered.SessionToken);
        await using NosumDbContext db = new(_database.Options);
        (await db.Players.SingleAsync(p => p.Id == login.PlayerId)).LastLoginAt.Should().Be(s_start.AddHours(1));
    }

    [Fact]
    public async Task LoginAsync_WrongPasswordOrUnknownUser_IsTheSameRefusal()
    {
        string name = Unique();
        _ = await _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None);

        AccountOutcome wrongPassword = await _accounts.LoginAsync(name, "password-2", CancellationToken.None);
        AccountOutcome unknownUser = await _accounts.LoginAsync(Unique(), "password-1", CancellationToken.None);

        wrongPassword.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidCredentials));
        unknownUser.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidCredentials));
    }

    [Fact]
    public async Task ResumeAsync_TokenFromRegister_SignsInSamePlayer()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", string.Empty, CancellationToken.None);

        AccountOutcome resumed = await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.Ok);
        resumed.PlayerId.Should().Be(registered.PlayerId);
        resumed.Username.Should().Be(registered.Username);
        resumed.ExpiresAt.Should().Be(registered.ExpiresAt);
    }

    [Fact]
    public async Task ResumeAsync_TokenFromLogin_SignsIn()
    {
        string name = Unique();
        _ = await _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None);
        AccountOutcome login = await _accounts.LoginAsync(name, "password-1", CancellationToken.None);

        AccountOutcome resumed = await _accounts.ResumeAsync(login.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ResumeAsync_AfterExpiry_IsInvalidToken()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", string.Empty, CancellationToken.None);
        _clock.Advance(AccountService.DefaultSessionLifetime + TimeSpan.FromSeconds(1));

        AccountOutcome resumed = await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Fact]
    public async Task ResumeAsync_RevokedToken_IsInvalidToken()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", string.Empty, CancellationToken.None);
        await using (NosumDbContext db = new(_database.Options))
        {
            PlayerSessionToken stored = await db.PlayerSessionTokens.SingleAsync(t => t.PlayerId == registered.PlayerId);
            stored.RevokedAt = s_start;
            _ = await db.SaveChangesAsync();
        }

        AccountOutcome resumed = await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task ResumeAsync_UnknownToken_IsInvalidToken(string token)
    {
        AccountOutcome resumed = await _accounts.ResumeAsync(token, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Fact]
    public async Task SetLanguageAsync_ValidTag_IsStoredAndReturnedByNextLogin()
    {
        string name = Unique();
        AccountOutcome registered = await _accounts.RegisterAsync(name, "password-1", string.Empty, CancellationToken.None);

        AccountStatus status = await _accounts.SetLanguageAsync(registered.PlayerId, "en", CancellationToken.None);
        AccountOutcome login = await _accounts.LoginAsync(name, "password-1", CancellationToken.None);

        status.Should().Be(AccountStatus.Ok);
        login.PreferredLanguage.Should().Be("en");
    }

    [Fact]
    public async Task SetLanguageAsync_MalformedTag_IsRefusedAndKeepsOldValue()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", string.Empty, CancellationToken.None);

        AccountStatus status = await _accounts.SetLanguageAsync(registered.PlayerId, "en_US!", CancellationToken.None);

        status.Should().Be(AccountStatus.InvalidLanguage);
        (await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None)).PreferredLanguage.Should().Be("vi");
    }

    [Fact]
    public async Task PlayerStateService_SaveThenLoad_RoundTripsAndOverwrites()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", string.Empty, CancellationToken.None);
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);

        PlayerStateRow? before = await states.LoadAsync(registered.PlayerId, CancellationToken.None);
        await states.SaveAsync(registered.PlayerId, new PlayerStateRow("Farm", 12.5, 40, Facing.Left), CancellationToken.None);
        await states.SaveAsync(registered.PlayerId, new PlayerStateRow("Town", 3, 4.25, Facing.Up), CancellationToken.None);
        PlayerStateRow? after = await states.LoadAsync(registered.PlayerId, CancellationToken.None);

        before.Should().BeNull();
        after.Should().Be(new PlayerStateRow("Town", 3, 4.25, Facing.Up));
    }

    private static string Unique() => "p" + Guid.NewGuid().ToString("N")[..12];
}
