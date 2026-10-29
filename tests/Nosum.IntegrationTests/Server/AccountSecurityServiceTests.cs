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

/// <summary>Session hardening (docs/adr/ADR-032 D5): rotation, idle window, session cap and the sweep, against a real PostgreSQL database.</summary>
public sealed class AccountSecurityServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly AccountService _accounts;

    public AccountSecurityServiceTests(PostgresFixture database)
    {
        _database = database;
        _accounts = new AccountService(
            new EfUnitOfWorkFactory(database.Options),
            FakeExternalTokenVerifier.All(),
            new SessionTokenService(),
            _clock);
    }

    [Fact]
    public async Task ResumeAsync_Success_RotatesTheTokenAndTheOldOneLivesOnlyThroughTheGrace()
    {
        AccountOutcome account = await SignInAsync();

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
    public async Task ResumeAsync_RotationReplyLost_OldTokenStillResumesAFewMinutesLater()
    {
        AccountOutcome account = await SignInAsync();

        // The reply to this resume never reached the client, so it keeps the token it had: a reconnect a few minutes later must still work (issue #171).
        _ = await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(10));
        (await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public void RotationGrace_IsFifteenMinutes()
    {
        AccountService.RotationGrace.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task ResumeAsync_EachResumeStartsAFreshThirtyDayIdleWindow()
    {
        AccountOutcome account = await SignInAsync();
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
    public async Task SignInWithExternalAsync_BeyondTheSessionCap_RevokesTheOldestTokens()
    {
        string subject = Guid.NewGuid().ToString("N");
        AccountOutcome first = await TestAccounts.SignInAsync(_accounts, subject);
        List<AccountOutcome> later = [];
        for (int i = 0; i < AccountService.MaxSessionsPerAccount; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            later.Add(await TestAccounts.SignInAsync(_accounts, subject));
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
        AccountOutcome account = await SignInAsync();
        _ = await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(8));
        GuestCleanupService cleanup = new(_database.Options, _clock);

        _ = await cleanup.RunOnceAsync(CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == account.PlayerId && t.RevokedAt != null)).Should().BeFalse();
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == account.PlayerId && t.RevokedAt == null)).Should().BeTrue();
    }

    private async Task<AccountOutcome> SignInAsync()
    {
        AccountOutcome account = await TestAccounts.SignInAsync(_accounts, Guid.NewGuid().ToString("N"), GameLanguage.English);
        account.Status.Should().Be(AccountStatus.Ok);
        return account;
    }
}
