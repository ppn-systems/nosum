// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
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

        AccountOutcome outcome = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.PlayerId.Should().NotBeEmpty();
        outcome.Email.Should().Be(name);
        outcome.SessionToken.Should().NotBeEmpty();
        outcome.ExpiresAt.Should().Be(s_start + AccountService.DefaultSessionLifetime);
        outcome.Language.Should().Be(GameLanguage.Vietnamese);
        outcome.IsGuest.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_Stored_KeepsOnlyHashesNeverPasswordOrToken()
    {
        string name = Unique();

        AccountOutcome outcome = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.English, CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        Player player = await db.Players.SingleAsync(p => p.Id == outcome.PlayerId);
        player.PasswordHash!.Should().StartWith("PBKDF2-SHA256$").And.NotContain("password-1");
        player.Language.Should().Be(GameLanguage.English);
        PlayerSessionToken stored = await db.PlayerSessionTokens.SingleAsync(t => t.PlayerId == outcome.PlayerId);
        stored.TokenHash.Should().NotBe(outcome.SessionToken).And.NotContain(outcome.SessionToken);
    }

    [Fact]
    public async Task RegisterAsync_MixedCaseEmail_IsStoredAndReturnedInLowerCase()
    {
        string name = Unique();

        AccountOutcome outcome = await _accounts.RegisterAsync(name.ToUpperInvariant(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome login = await _accounts.LoginAsync(name, "password-1", CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.Email.Should().Be(name);
        login.PlayerId.Should().Be(outcome.PlayerId);
    }

    [Fact]
    public async Task RegisterAsync_SameUsernameDifferentCase_IsUsernameTaken()
    {
        string name = Unique();
        _ = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome second = await _accounts.RegisterAsync(name.ToUpperInvariant(), "password-2", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        second.Status.Should().Be(AccountStatus.EmailTaken);
        second.SessionToken.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterAsync_SameUsernameConcurrently_CreatesExactlyOne()
    {
        string name = Unique();

        AccountOutcome[] results = await Task.WhenAll(
            _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None),
            _accounts.RegisterAsync(name, "password-2", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None),
            _accounts.RegisterAsync(name, "password-3", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None));

        results.Should().ContainSingle(r => r.Status == AccountStatus.Ok);
        results.Should().OnlyContain(r => r.Status == AccountStatus.Ok || r.Status == AccountStatus.EmailTaken);
    }

    [Theory]
    [InlineData("farmer", "password-1", GameLanguage.Vietnamese, AccountStatus.InvalidEmail)]
    [InlineData("farmer@yahoo.com", "password-1", GameLanguage.Vietnamese, AccountStatus.InvalidEmail)]
    [InlineData("abc@gmail.com", "password-1", GameLanguage.Vietnamese, AccountStatus.InvalidEmail)]
    [InlineData("validname@gmail.com", "short", GameLanguage.Vietnamese, AccountStatus.InvalidPassword)]
    [InlineData("validname@gmail.com", "password-1", (GameLanguage)99, AccountStatus.InvalidLanguage)]
    public async Task RegisterAsync_BreaksARule_IsRefusedWithMatchingCode(string name, string password, GameLanguage language, AccountStatus expected)
    {
        AccountOutcome outcome = await _accounts.RegisterAsync(name, password, TestNicknames.Next(), language, CancellationToken.None);

        outcome.Status.Should().Be(expected);
        outcome.SessionToken.Should().BeEmpty();
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_IssuesNewTokenAndRecordsLastLogin()
    {
        string name = Unique();
        AccountOutcome registered = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
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
        _ = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome wrongPassword = await _accounts.LoginAsync(name, "password-2", CancellationToken.None);
        AccountOutcome unknownUser = await _accounts.LoginAsync(Unique(), "password-1", CancellationToken.None);

        wrongPassword.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidCredentials));
        unknownUser.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidCredentials));
    }

    [Fact]
    public async Task ResumeAsync_TokenFromRegister_SignsInSamePlayer()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome resumed = await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.Ok);
        resumed.PlayerId.Should().Be(registered.PlayerId);
        resumed.Email.Should().Be(registered.Email);
        resumed.ExpiresAt.Should().Be(registered.ExpiresAt);
    }

    [Fact]
    public async Task ResumeAsync_TokenFromLogin_SignsIn()
    {
        string name = Unique();
        _ = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome login = await _accounts.LoginAsync(name, "password-1", CancellationToken.None);

        AccountOutcome resumed = await _accounts.ResumeAsync(login.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ResumeAsync_AfterExpiry_IsInvalidToken()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        _clock.Advance(AccountService.DefaultSessionLifetime + TimeSpan.FromSeconds(1));

        AccountOutcome resumed = await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Fact]
    public async Task ResumeAsync_RevokedToken_IsInvalidToken()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
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
        AccountOutcome registered = await _accounts.RegisterAsync(name, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        AccountStatus status = await _accounts.SetLanguageAsync(registered.PlayerId, GameLanguage.English, CancellationToken.None);
        AccountOutcome login = await _accounts.LoginAsync(name, "password-1", CancellationToken.None);

        status.Should().Be(AccountStatus.Ok);
        login.Language.Should().Be(GameLanguage.English);
    }

    [Fact]
    public async Task SetLanguageAsync_MalformedTag_IsRefusedAndKeepsOldValue()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        AccountStatus status = await _accounts.SetLanguageAsync(registered.PlayerId, (GameLanguage)7, CancellationToken.None);

        status.Should().Be(AccountStatus.InvalidLanguage);
        (await _accounts.ResumeAsync(registered.SessionToken, CancellationToken.None)).Language.Should().Be(GameLanguage.Vietnamese);
    }

    [Fact]
    public async Task StartGuestAsync_CreatesGuestWithoutEmailAndTheTokenResumes()
    {
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None);
        AccountOutcome resumed = await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None);

        guest.Status.Should().Be(AccountStatus.Ok);
        guest.IsGuest.Should().BeTrue();
        guest.Email.Should().BeEmpty();
        guest.Language.Should().Be(GameLanguage.English);
        resumed.PlayerId.Should().Be(guest.PlayerId);
        resumed.IsGuest.Should().BeTrue();
        await using NosumDbContext db = new(_database.Options);
        Player stored = await db.Players.SingleAsync(p => p.Id == guest.PlayerId);
        stored.Email.Should().BeNull();
        stored.PasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task StartGuestAsync_TwoGuests_AreTwoPlayers()
    {
        AccountOutcome first = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome second = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        second.PlayerId.Should().NotBe(first.PlayerId);
    }

    [Fact]
    public async Task UpgradeGuestAsync_KeepsPlayerIdAndStateAndRevokesGuestToken()
    {
        string email = Unique();
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        await states.SaveAsync(guest.PlayerId, new PlayerStateRow("Farm", 5, 6, Facing.Right), CancellationToken.None);

        AccountOutcome upgraded = await _accounts.UpgradeGuestAsync(guest.PlayerId, guest.SessionId, email, "password-1", TestNicknames.Next(), CancellationToken.None);
        AccountOutcome guestToken = await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None);
        AccountOutcome newToken = await _accounts.ResumeAsync(upgraded.SessionToken, CancellationToken.None);
        AccountOutcome login = await _accounts.LoginAsync(email, "password-1", CancellationToken.None);

        upgraded.Status.Should().Be(AccountStatus.Ok);
        upgraded.PlayerId.Should().Be(guest.PlayerId);
        upgraded.IsGuest.Should().BeFalse();
        upgraded.Email.Should().Be(email);
        upgraded.SessionToken.Should().NotBe(guest.SessionToken);
        guestToken.Status.Should().Be(AccountStatus.InvalidToken);
        newToken.PlayerId.Should().Be(guest.PlayerId);
        login.PlayerId.Should().Be(guest.PlayerId);
        (await states.LoadAsync(guest.PlayerId, CancellationToken.None)).Should().Be(new PlayerStateRow("Farm", 5, 6, Facing.Right));
    }

    [Fact]
    public async Task UpgradeGuestAsync_RegisteredPlayer_IsRefusedAsNotGuest()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome outcome = await _accounts.UpgradeGuestAsync(registered.PlayerId, registered.SessionId, Unique(), "password-2", TestNicknames.Next(), CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.NotGuest);
    }

    [Fact]
    public async Task UpgradeGuestAsync_EmailAlreadyRegistered_IsRefusedAndStaysGuest()
    {
        string taken = Unique();
        _ = await _accounts.RegisterAsync(taken, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome outcome = await _accounts.UpgradeGuestAsync(guest.PlayerId, guest.SessionId, taken, "password-2", TestNicknames.Next(), CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.EmailTaken);
        (await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None)).IsGuest.Should().BeTrue();
    }

    [Theory]
    [InlineData("nope", "password-1", AccountStatus.InvalidEmail)]
    [InlineData("validname@gmail.com", "short", AccountStatus.InvalidPassword)]
    public async Task UpgradeGuestAsync_InvalidInput_IsRefusedAndStaysGuest(string email, string password, AccountStatus expected)
    {
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome outcome = await _accounts.UpgradeGuestAsync(guest.PlayerId, guest.SessionId, email, password, TestNicknames.Next(), CancellationToken.None);

        outcome.Status.Should().Be(expected);
        (await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task LogoutAsync_RevokesOnlyThatSession()
    {
        string email = Unique();
        AccountOutcome first = await _accounts.RegisterAsync(email, "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome second = await _accounts.LoginAsync(email, "password-1", CancellationToken.None);

        await _accounts.LogoutAsync(first.SessionId, CancellationToken.None);

        (await _accounts.ResumeAsync(first.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
        (await _accounts.ResumeAsync(second.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerSessionTokens.SingleAsync(t => t.Id == first.SessionId)).RevokedAt.Should().Be(s_start);
    }

    [Fact]
    public async Task UnlockAsync_Guest_UnlocksOnceAndAccountResultReportsIt()
    {
        PlayerLifecycleService lifecycle = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        bool first = await lifecycle.UnlockAsync(guest.PlayerId, CancellationToken.None);
        bool second = await lifecycle.UnlockAsync(guest.PlayerId, CancellationToken.None);
        AccountOutcome resumed = await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None);

        guest.RegistrationPromptUnlocked.Should().BeFalse();
        first.Should().BeTrue();
        second.Should().BeFalse();
        resumed.RegistrationPromptUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task UnlockAsync_RegisteredPlayer_DoesNothing()
    {
        PlayerLifecycleService lifecycle = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);

        (await lifecycle.UnlockAsync(registered.PlayerId, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task GuestCleanup_DeletesOnlyIdleGuestsWithTheirStateAndTokens()
    {
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        PlayerLifecycleService lifecycle = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        AccountOutcome oldGuest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome oldRegistered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        await states.SaveAsync(oldGuest.PlayerId, new PlayerStateRow("Farm", 1, 2, Facing.Down), CancellationToken.None);

        _clock.Advance(TimeSpan.FromDays(31));
        AccountOutcome recentGuest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome returningGuest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(-5));
        await lifecycle.TouchAsync(returningGuest.PlayerId, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(5));

        GuestCleanupService cleanup = new(_database.Options, _clock);
        _ = await cleanup.RunOnceAsync(CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        (await db.Players.AnyAsync(p => p.Id == oldGuest.PlayerId)).Should().BeFalse();
        (await db.PlayerStates.AnyAsync(s => s.PlayerId == oldGuest.PlayerId)).Should().BeFalse();
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == oldGuest.PlayerId)).Should().BeFalse();
        (await db.Players.AnyAsync(p => p.Id == oldRegistered.PlayerId)).Should().BeTrue();
        (await db.Players.AnyAsync(p => p.Id == recentGuest.PlayerId)).Should().BeTrue();
        (await db.Players.AnyAsync(p => p.Id == returningGuest.PlayerId)).Should().BeTrue();
    }

    [Fact]
    public async Task ResumeAsync_Guest_RefreshesLastActivity_SoTheGuestIsKept()
    {
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(29));
        _ = await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(29));

        _ = await new GuestCleanupService(_database.Options, _clock).RunOnceAsync(CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        (await db.Players.AnyAsync(p => p.Id == guest.PlayerId)).Should().BeTrue();
    }

    [Fact]
    public async Task PlayerStateService_SaveThenLoad_RoundTripsAndOverwrites()
    {
        AccountOutcome registered = await _accounts.RegisterAsync(Unique(), "password-1", TestNicknames.Next(), GameLanguage.Unspecified, CancellationToken.None);
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);

        PlayerStateRow? before = await states.LoadAsync(registered.PlayerId, CancellationToken.None);
        await states.SaveAsync(registered.PlayerId, new PlayerStateRow("Farm", 12.5, 40, Facing.Left), CancellationToken.None);
        await states.SaveAsync(registered.PlayerId, new PlayerStateRow("Town", 3, 4.25, Facing.Up), CancellationToken.None);
        PlayerStateRow? after = await states.LoadAsync(registered.PlayerId, CancellationToken.None);

        before.Should().BeNull();
        after.Should().Be(new PlayerStateRow("Town", 3, 4.25, Facing.Up));
    }

    private static string Unique() => "p" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
}
