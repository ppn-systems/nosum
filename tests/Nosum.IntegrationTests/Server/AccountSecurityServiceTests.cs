// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Password change and session hardening (docs/adr/ADR-032 D5) against a real PostgreSQL database.</summary>
public sealed class AccountSecurityServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "password-1";

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly AccountService _accounts;

    public AccountSecurityServiceTests(PostgresFixture database)
    {
        _database = database;
        _accounts = new AccountService(
            new EfUnitOfWorkFactory(database.Options),
            new Pbkdf2PasswordHasher(iterations: 1000),
            new SessionTokenService(),
            _clock);
    }

    [Fact]
    public async Task ChangePasswordAsync_RightCurrentPassword_KeepsOnlyTheSessionInUse()
    {
        (string email, AccountOutcome first) = await RegisterAsync();
        AccountOutcome other = await _accounts.LoginAsync(email, Password, CancellationToken.None);

        AccountStatus status = await _accounts.ChangePasswordAsync(first.PlayerId, first.SessionId, Password, "new-password-2", CancellationToken.None);

        status.Should().Be(AccountStatus.Ok);
        (await _accounts.ResumeAsync(other.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
        (await _accounts.ResumeAsync(first.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
        (await _accounts.LoginAsync(email, "new-password-2", CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPasswordWeakNewPasswordAndGuest_AreRefused()
    {
        (_, AccountOutcome account) = await RegisterAsync();
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None);

        (await _accounts.ChangePasswordAsync(account.PlayerId, account.SessionId, "not-the-password", "new-password-2", CancellationToken.None)).Should().Be(AccountStatus.InvalidCredentials);
        (await _accounts.ChangePasswordAsync(account.PlayerId, account.SessionId, Password, "short", CancellationToken.None)).Should().Be(AccountStatus.InvalidPassword);
        (await _accounts.ChangePasswordAsync(guest.PlayerId, guest.SessionId, Password, "new-password-2", CancellationToken.None)).Should().Be(AccountStatus.NotGuest);
    }

    [Fact]
    public async Task ResumeAsync_Success_RotatesTheTokenAndTheOldOneLivesOnlyThroughTheGrace()
    {
        (_, AccountOutcome account) = await RegisterAsync();

        AccountOutcome resumed = await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.Ok);
        resumed.SessionToken.Should().NotBe(account.SessionToken);
        resumed.SessionId.Should().NotBe(account.SessionId);
        resumed.PlayerId.Should().Be(account.PlayerId);
        resumed.ExpiresAt.Should().Be(s_start + AccountService.DefaultSessionLifetime);

        _clock.Advance(AccountService.RotationGrace - TimeSpan.FromSeconds(1));
        (await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);

        _clock.Advance(AccountService.RotationGrace);
        (await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
        (await _accounts.ResumeAsync(resumed.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ResumeAsync_EachResumeStartsAFreshThirtyDayIdleWindow()
    {
        (_, AccountOutcome account) = await RegisterAsync();
        string token = account.SessionToken;

        for (int i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromDays(20));
            AccountOutcome resumed = await _accounts.ResumeAsync(token, CancellationToken.None);
            resumed.Status.Should().Be(AccountStatus.Ok);
            token = resumed.SessionToken;
        }

        _clock.Advance(TimeSpan.FromDays(31));
        (await _accounts.ResumeAsync(token, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Fact]
    public async Task LoginAsync_BeyondTheSessionCap_RevokesTheOldestTokens()
    {
        (string email, AccountOutcome first) = await RegisterAsync();
        List<AccountOutcome> later = [];
        for (int i = 0; i < AccountService.MaxSessionsPerAccount; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            later.Add(await _accounts.LoginAsync(email, Password, CancellationToken.None));
        }

        await using NosumDbContext db = new(_database.Options);
        List<PlayerSessionToken> live = await db.PlayerSessionTokens.Where(t => t.PlayerId == first.PlayerId && t.RevokedAt == null).ToListAsync();
        live.Should().HaveCount(AccountService.MaxSessionsPerAccount);
        (await _accounts.ResumeAsync(first.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
        (await _accounts.ResumeAsync(later[^1].SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task RunOnceAsync_SweepsOldRevokedTokens()
    {
        (_, AccountOutcome account) = await RegisterAsync();
        _ = await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(8));
        GuestCleanupService cleanup = new(_database.Options, _clock);

        _ = await cleanup.RunOnceAsync(CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == account.PlayerId && t.RevokedAt != null)).Should().BeFalse();
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == account.PlayerId && t.RevokedAt == null)).Should().BeTrue();
    }

    private async Task<(string Email, AccountOutcome Account)> RegisterAsync()
    {
        string email = Unique();
        AccountOutcome account = await _accounts.RegisterAsync(email, Password, TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        account.Status.Should().Be(AccountStatus.Ok);
        return (email, account);
    }

    private static string Unique() => "s" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
}
