// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Email;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Domain.Accounts;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Account recovery and session hardening (docs/adr/ADR-032 D4, D5) against a real PostgreSQL database.</summary>
public sealed class AccountSecurityServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private const string Password = "password-1";

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly RecordingSender _mail = new();
    private readonly AccountService _accounts;

    public AccountSecurityServiceTests(PostgresFixture database)
    {
        _database = database;
        _accounts = new AccountService(
            new EfUnitOfWorkFactory(database.Options),
            new Pbkdf2PasswordHasher(iterations: 1000),
            new SessionTokenService(),
            _clock,
            email: _mail);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_RegisteredAccount_MailsAnEightDigitCodeStoredOnlyAsHash()
    {
        (string email, AccountOutcome account) = await RegisterAsync();

        AccountStatus status = await _accounts.RequestPasswordResetAsync(email.ToUpperInvariant(), CancellationToken.None);

        status.Should().Be(AccountStatus.Ok);
        EmailMessage message = _mail.Sent.Should().ContainSingle().Subject;
        message.Kind.Should().Be(EmailKind.PasswordReset);
        message.To.Should().Be(email);
        message.Code.Should().MatchRegex("^[0-9]{8}$");
        await using NosumDbContext db = new(_database.Options);
        PasswordResetCode stored = await db.PasswordResetCodes.SingleAsync(c => c.PlayerId == account.PlayerId);
        stored.CodeHash.Should().StartWith("PBKDF2-SHA256$").And.NotContain(message.Code);
        stored.ExpiresAt.Should().Be(s_start + AccountService.ResetCodeLifetime);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_UnknownAddressMalformedAddressAndGuest_NeverSendAndNeverReveal()
    {
        AccountStatus unknown = await _accounts.RequestPasswordResetAsync(Unique(), CancellationToken.None);
        AccountStatus malformed = await _accounts.RequestPasswordResetAsync("not-an-address", CancellationToken.None);
        _ = await _accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None);

        unknown.Should().Be(AccountStatus.Ok);
        malformed.Should().Be(AccountStatus.InvalidEmail);
        _mail.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestPasswordResetAsync_FourthRequestInAnHour_AnswersOkButSendsNothingUntilTheHourPasses()
    {
        (string email, _) = await RegisterAsync();

        for (int i = 0; i < AccountService.ResetRequestsPerHour + 1; i++)
        {
            (await _accounts.RequestPasswordResetAsync(email, CancellationToken.None)).Should().Be(AccountStatus.Ok);
        }

        _mail.Sent.Should().HaveCount(AccountService.ResetRequestsPerHour);

        _clock.Advance(TimeSpan.FromMinutes(61));
        _ = await _accounts.RequestPasswordResetAsync(email, CancellationToken.None);

        _mail.Sent.Should().HaveCount(AccountService.ResetRequestsPerHour + 1);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_MailProviderFails_StillAnswersOk()
    {
        AccountService failing = new(
            new EfUnitOfWorkFactory(_database.Options), new Pbkdf2PasswordHasher(iterations: 1000), new SessionTokenService(), _clock, email: new ThrowingSender());
        string email = Unique();
        _ = await failing.RegisterAsync(email, Password, TestNicknames.Next(), GameLanguage.English, CancellationToken.None);

        AccountStatus status = await failing.RequestPasswordResetAsync(email, CancellationToken.None);

        status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_RightCode_SetsThePasswordAndEndsEverySession()
    {
        (string email, AccountOutcome account) = await RegisterAsync();
        AccountOutcome second = await _accounts.LoginAsync(email, Password, CancellationToken.None);
        string code = await RequestAsync(email);

        PasswordResetOutcome outcome = await _accounts.ConfirmPasswordResetAsync(email, code, "new-password-2", CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.PlayerId.Should().Be(account.PlayerId);
        (await _accounts.LoginAsync(email, Password, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidCredentials);
        (await _accounts.LoginAsync(email, "new-password-2", CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
        (await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
        (await _accounts.ResumeAsync(second.SessionToken, CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_CodeUsedTwice_RefusesTheSecondUse()
    {
        (string email, _) = await RegisterAsync();
        string code = await RequestAsync(email);
        _ = await _accounts.ConfirmPasswordResetAsync(email, code, "new-password-2", CancellationToken.None);

        PasswordResetOutcome again = await _accounts.ConfirmPasswordResetAsync(email, code, "other-password-3", CancellationToken.None);

        again.Status.Should().Be(AccountStatus.InvalidResetCode);
        (await _accounts.LoginAsync(email, "new-password-2", CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_AfterFifteenMinutes_RefusesTheCode()
    {
        (string email, _) = await RegisterAsync();
        string code = await RequestAsync(email);
        _clock.Advance(AccountService.ResetCodeLifetime + TimeSpan.FromSeconds(1));

        PasswordResetOutcome outcome = await _accounts.ConfirmPasswordResetAsync(email, code, "new-password-2", CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.InvalidResetCode);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_FiveWrongTries_LockTheCodeEvenForTheRightOne()
    {
        (string email, _) = await RegisterAsync();
        string code = await RequestAsync(email);
        string wrong = code == "00000000" ? "11111111" : "00000000";

        for (int i = 0; i < AccountService.ResetMaxAttempts; i++)
        {
            (await _accounts.ConfirmPasswordResetAsync(email, wrong, "new-password-2", CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidResetCode);
        }

        PasswordResetOutcome right = await _accounts.ConfirmPasswordResetAsync(email, code, "new-password-2", CancellationToken.None);

        right.Status.Should().Be(AccountStatus.InvalidResetCode);
        (await _accounts.LoginAsync(email, Password, CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_NewRequest_ReplacesTheEarlierCode()
    {
        (string email, _) = await RegisterAsync();
        string first = await RequestAsync(email);
        string second = await RequestAsync(email);

        (await _accounts.ConfirmPasswordResetAsync(email, first, "new-password-2", CancellationToken.None)).Status
            .Should().Be(first == second ? AccountStatus.Ok : AccountStatus.InvalidResetCode);
        PasswordResetOutcome current = await _accounts.ConfirmPasswordResetAsync(email, second, "new-password-3", CancellationToken.None);

        current.Status.Should().Be(first == second ? AccountStatus.InvalidResetCode : AccountStatus.Ok);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_CodeOfAnotherAccount_IsRefused()
    {
        (string email, _) = await RegisterAsync();
        (string other, _) = await RegisterAsync();
        string code = await RequestAsync(email);

        PasswordResetOutcome outcome = await _accounts.ConfirmPasswordResetAsync(other, code, "new-password-2", CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.InvalidResetCode);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WeakNewPasswordOrMalformedCode_IsRefusedWithoutSpendingTheCode()
    {
        (string email, _) = await RegisterAsync();
        string code = await RequestAsync(email);

        (await _accounts.ConfirmPasswordResetAsync(email, code, "short", CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidPassword);
        (await _accounts.ConfirmPasswordResetAsync(email, "12ab5678", "new-password-2", CancellationToken.None)).Status.Should().Be(AccountStatus.InvalidResetCode);
        (await _accounts.ConfirmPasswordResetAsync(email, code, "new-password-2", CancellationToken.None)).Status.Should().Be(AccountStatus.Ok);
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
    public async Task RunOnceAsync_SweepsOldRevokedTokensAndExpiredCodes()
    {
        (string email, AccountOutcome account) = await RegisterAsync();
        _ = await RequestAsync(email);
        _ = await _accounts.ResumeAsync(account.SessionToken, CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(8));
        GuestCleanupService cleanup = new(_database.Options, _clock);

        _ = await cleanup.RunOnceAsync(CancellationToken.None);

        await using NosumDbContext db = new(_database.Options);
        (await db.PasswordResetCodes.AnyAsync(c => c.PlayerId == account.PlayerId)).Should().BeFalse();
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == account.PlayerId && t.RevokedAt != null)).Should().BeFalse();
        (await db.PlayerSessionTokens.AnyAsync(t => t.PlayerId == account.PlayerId && t.RevokedAt == null)).Should().BeTrue();
    }

    private async Task<string> RequestAsync(string email)
    {
        int before = _mail.Sent.Count;
        _ = await _accounts.RequestPasswordResetAsync(email, CancellationToken.None);
        _mail.Sent.Should().HaveCount(before + 1);
        return _mail.Sent[^1].Code;
    }

    private async Task<(string Email, AccountOutcome Account)> RegisterAsync()
    {
        string email = Unique();
        AccountOutcome account = await _accounts.RegisterAsync(email, Password, TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        account.Status.Should().Be(AccountStatus.Ok);
        return (email, account);
    }

    private static string Unique() => "s" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            this.Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => throw new InvalidOperationException("provider down");
    }
}
