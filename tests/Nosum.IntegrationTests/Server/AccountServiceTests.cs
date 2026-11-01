// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Economy;
using Nosum.Gameplay.Items;
using Nosum.Abstractions.Identity;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Contracts.Validation;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Account services (docs/adr/ADR-069) against a real PostgreSQL database.</summary>
public sealed class AccountServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly TransferService _transfers;
    private readonly AccountService _accounts;

    public AccountServiceTests(PostgresFixture database)
    {
        _database = database;
        EfUnitOfWorkFactory units = new(database.Options);
        _accounts = new AccountService(
            units,
            FakeExternalTokenVerifier.All(),
            new SessionTokenService(),
            _clock);
        _transfers = new TransferService(new InventoryService(units, _clock), _clock);
        _accounts.AttachTransfers(_transfers);
    }

    [Fact]
    public async Task SignInWithExternalAsync_NewSubject_CreatesRegisteredPlayerWithNeutralName()
    {
        AccountOutcome outcome = await TestAccounts.SignInAsync(_accounts, Unique(), GameLanguage.English);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.PlayerId.Should().NotBeEmpty();
        outcome.SessionToken.Should().NotBeEmpty();
        outcome.ExpiresAt.Should().Be(s_start + AccountService.DefaultSessionLifetime);
        outcome.IsGuest.Should().BeFalse();
        outcome.Language.Should().Be(GameLanguage.English);
        NicknameRules.IsGuestName(outcome.Nickname, out _).Should().BeTrue();
    }

    [Fact]
    public async Task SignInWithExternalAsync_UnspecifiedLanguage_UsesVietnamese()
    {
        AccountOutcome outcome = await TestAccounts.SignInAsync(_accounts, Unique(), GameLanguage.Unspecified);

        outcome.Language.Should().Be(GameLanguage.Vietnamese);
    }

    [Fact]
    public async Task SignInWithExternalAsync_Stored_LinksTheSubjectAndKeepsOnlyTheSessionHash()
    {
        string subject = Unique();

        AccountOutcome outcome = await TestAccounts.SignInAsync(_accounts, subject, GameLanguage.English);

        await using NosumDbContext db = new(_database.Options);
        ExternalLogin link = await db.ExternalLogins.SingleAsync(l => l.PlayerId == outcome.PlayerId);
        link.Provider.Should().Be(ExternalProvider.Google);
        link.Subject.Should().Be(subject);
        PlayerSessionToken stored = await db.PlayerSessionTokens.SingleAsync(t => t.PlayerId == outcome.PlayerId);
        stored.TokenHash.Should().NotBe(outcome.SessionToken).And.NotContain(outcome.SessionToken);
    }

    [Fact]
    public async Task SignInWithExternalAsync_SameSubjectAgain_SignsInTheSamePlayer()
    {
        string subject = Unique();
        AccountOutcome first = await TestAccounts.SignInAsync(_accounts, subject);
        _clock.Advance(TimeSpan.FromHours(1));

        AccountOutcome again = await TestAccounts.SignInAsync(_accounts, subject);

        again.Status.Should().Be(AccountStatus.Ok);
        again.PlayerId.Should().Be(first.PlayerId);
        again.SessionToken.Should().NotBe(first.SessionToken);
        await using NosumDbContext db = new(_database.Options);
        (await db.Players.SingleAsync(p => p.Id == again.PlayerId)).LastLoginAt.Should().Be(s_start.AddHours(1));
    }

    [Fact]
    public async Task SignInWithExternalAsync_SameSubjectConcurrently_CreatesExactlyOnePlayer()
    {
        string subject = Unique();

        AccountOutcome[] results = await Task.WhenAll(
            TestAccounts.SignInAsync(_accounts, subject),
            TestAccounts.SignInAsync(_accounts, subject),
            TestAccounts.SignInAsync(_accounts, subject));

        results.Should().OnlyContain(r => r.Status == AccountStatus.Ok);
        results.Select(r => r.PlayerId).Distinct().Should().ContainSingle();
        await using NosumDbContext db = new(_database.Options);
        (await db.ExternalLogins.CountAsync(l => l.Subject == subject)).Should().Be(1);
    }

    [Fact]
    public async Task SignInWithExternalAsync_SameSubjectUnderOtherProvider_IsAnotherPlayer()
    {
        string subject = Unique();
        AccountOutcome google = await TestAccounts.SignInAsync(_accounts, subject);

        AccountOutcome microsoft = await _accounts.SignInWithExternalAsync(ExternalProvider.Microsoft, FakeExternalTokenVerifier.Token(ExternalProvider.Microsoft, subject), GameLanguage.English, CancellationToken.None);

        microsoft.Status.Should().Be(AccountStatus.Ok);
        microsoft.PlayerId.Should().NotBe(google.PlayerId);
    }

    [Fact]
    public async Task SignInWithExternalAsync_BannedAccount_IsRefusedAsBanned()
    {
        string subject = Unique();
        AccountOutcome account = await TestAccounts.SignInAsync(_accounts, subject);
        await using (NosumDbContext db = new(_database.Options))
        {
            Player player = await db.Players.SingleAsync(p => p.Id == account.PlayerId);
            player.BannedAt = s_start;
            _ = await db.SaveChangesAsync();
        }

        AccountOutcome outcome = await TestAccounts.SignInAsync(_accounts, subject);

        outcome.Status.Should().Be(AccountStatus.Banned);
        outcome.SessionToken.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    [InlineData("fake:Google:")]
    [InlineData("fake:Microsoft:someone")]
    public async Task SignInWithExternalAsync_TokenTheVerifierRejects_IsInvalidIdToken(string idToken)
    {
        AccountOutcome outcome = await _accounts.SignInWithExternalAsync(ExternalProvider.Google, idToken, GameLanguage.English, CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidIdToken));
    }

    [Fact]
    public async Task SignInWithExternalAsync_TokenLongerThanTheWireLimit_IsInvalidIdToken()
    {
        string tooLong = "fake:Google:" + new string('a', WireLimits.IdToken);

        AccountOutcome outcome = await _accounts.SignInWithExternalAsync(ExternalProvider.Google, tooLong, GameLanguage.English, CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidIdToken));
    }

    [Fact]
    public async Task SignInWithExternalAsync_ProviderWithoutVerifier_IsInvalidIdToken()
    {
        AccountService googleOnly = new(
            new EfUnitOfWorkFactory(_database.Options),
            [new FakeExternalTokenVerifier(ExternalProvider.Google)],
            new SessionTokenService(),
            _clock);
        string subject = Unique();

        AccountOutcome outcome = await googleOnly.SignInWithExternalAsync(ExternalProvider.Microsoft, FakeExternalTokenVerifier.Token(ExternalProvider.Microsoft, subject), GameLanguage.English, CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidIdToken));
    }

    [Fact]
    public async Task SignInWithExternalAsync_UnknownLanguage_IsInvalidLanguage()
    {
        AccountOutcome outcome = await TestAccounts.SignInAsync(_accounts, Unique(), (GameLanguage)99);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidLanguage));
    }

    [Fact]
    public async Task ResumeAsync_TokenFromSignIn_SignsInSamePlayer()
    {
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());

        AccountOutcome resumed = await _accounts.ResumeAsync(signedIn.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.Ok);
        resumed.PlayerId.Should().Be(signedIn.PlayerId);
        resumed.Nickname.Should().Be(signedIn.Nickname);
        resumed.IsGuest.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeAsync_AfterExpiry_IsInvalidToken()
    {
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());
        _clock.Advance(AccountService.DefaultSessionLifetime + TimeSpan.FromSeconds(1));

        AccountOutcome resumed = await _accounts.ResumeAsync(signedIn.SessionToken, CancellationToken.None);

        resumed.Status.Should().Be(AccountStatus.InvalidToken);
    }

    [Fact]
    public async Task ResumeAsync_RevokedToken_IsInvalidToken()
    {
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());
        await using (NosumDbContext db = new(_database.Options))
        {
            PlayerSessionToken stored = await db.PlayerSessionTokens.SingleAsync(t => t.PlayerId == signedIn.PlayerId);
            stored.RevokedAt = s_start;
            _ = await db.SaveChangesAsync();
        }

        AccountOutcome resumed = await _accounts.ResumeAsync(signedIn.SessionToken, CancellationToken.None);

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
    public async Task SetLanguageAsync_ValidTag_IsStoredAndReturnedByNextSignIn()
    {
        string subject = Unique();
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, subject);

        AccountStatus status = await _accounts.SetLanguageAsync(signedIn.PlayerId, GameLanguage.English, CancellationToken.None);
        AccountOutcome again = await TestAccounts.SignInAsync(_accounts, subject, GameLanguage.Vietnamese);

        status.Should().Be(AccountStatus.Ok);
        again.Language.Should().Be(GameLanguage.English);
    }

    [Fact]
    public async Task SetLanguageAsync_MalformedTag_IsRefusedAndKeepsOldValue()
    {
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique(), GameLanguage.English);

        AccountStatus status = await _accounts.SetLanguageAsync(signedIn.PlayerId, (GameLanguage)7, CancellationToken.None);

        status.Should().Be(AccountStatus.InvalidLanguage);
        (await _accounts.ResumeAsync(signedIn.SessionToken, CancellationToken.None)).Language.Should().Be(GameLanguage.English);
    }

    [Fact]
    public async Task StartGuestAsync_CreatesGuestWithoutIdentityAndTheTokenResumes()
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
        (await db.ExternalLogins.AnyAsync(l => l.PlayerId == guest.PlayerId)).Should().BeFalse();
    }

    [Fact]
    public async Task StartGuestAsync_TwoGuests_AreTwoPlayers()
    {
        AccountOutcome first = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome second = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        second.PlayerId.Should().NotBe(first.PlayerId);
    }

    [Fact]
    public async Task LinkExternalIdentityAsync_Guest_KeepsPlayerIdAndStateAndRevokesGuestToken()
    {
        string subject = Unique();
        string nickname = TestNicknames.Next();
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        await states.SaveAsync(guest.PlayerId, new PlayerStateRow("Farm", 5, 6, Facing.Right), CancellationToken.None);

        AccountOutcome linked = await _accounts.LinkExternalIdentityAsync(guest.PlayerId, guest.SessionId, ExternalProvider.Google, FakeExternalTokenVerifier.Token(ExternalProvider.Google, subject), nickname, CancellationToken.None);
        AccountOutcome guestToken = await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None);
        AccountOutcome newToken = await _accounts.ResumeAsync(linked.SessionToken, CancellationToken.None);
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, subject);

        linked.Status.Should().Be(AccountStatus.Ok);
        linked.PlayerId.Should().Be(guest.PlayerId);
        linked.IsGuest.Should().BeFalse();
        linked.Nickname.Should().Be(nickname);
        linked.SessionToken.Should().NotBe(guest.SessionToken);
        guestToken.Status.Should().Be(AccountStatus.InvalidToken);
        newToken.PlayerId.Should().Be(guest.PlayerId);
        signedIn.PlayerId.Should().Be(guest.PlayerId);
        (await states.LoadAsync(guest.PlayerId, CancellationToken.None)).Should().Be(new PlayerStateRow("Farm", 5, 6, Facing.Right));
    }

    [Fact]
    public async Task LinkExternalIdentityAsync_RegisteredPlayer_IsRefusedAsNotGuest()
    {
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());

        AccountOutcome outcome = await _accounts.LinkExternalIdentityAsync(signedIn.PlayerId, signedIn.SessionId, ExternalProvider.Google, FakeExternalTokenVerifier.Token(ExternalProvider.Google, Unique()), TestNicknames.Next(), CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.NotGuest));
    }

    [Fact]
    public async Task LinkExternalIdentityAsync_SubjectOfAnotherAccount_IsIdentityTakenAndStaysGuest()
    {
        string taken = Unique();
        _ = await TestAccounts.SignInAsync(_accounts, taken);
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome outcome = await _accounts.LinkExternalIdentityAsync(guest.PlayerId, guest.SessionId, ExternalProvider.Google, FakeExternalTokenVerifier.Token(ExternalProvider.Google, taken), TestNicknames.Next(), CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.IdentityTaken));
        (await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None)).IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task LinkExternalIdentityAsync_NicknameTaken_IsNicknameTakenAndStaysGuest()
    {
        string nickname = TestNicknames.Next();
        _ = await TestAccounts.RegisterAsync(_accounts, Unique(), nickname);
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome outcome = await _accounts.LinkExternalIdentityAsync(guest.PlayerId, guest.SessionId, ExternalProvider.Google, FakeExternalTokenVerifier.Token(ExternalProvider.Google, Unique()), nickname.ToUpperInvariant(), CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.NicknameTaken));
        (await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None)).IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task LinkExternalIdentityAsync_TokenTheVerifierRejects_IsInvalidIdTokenAndStaysGuest()
    {
        AccountOutcome guest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);

        AccountOutcome outcome = await _accounts.LinkExternalIdentityAsync(guest.PlayerId, guest.SessionId, ExternalProvider.Google, "not-a-real-token", TestNicknames.Next(), CancellationToken.None);

        outcome.Should().Be(AccountOutcome.Refused(AccountStatus.InvalidIdToken));
        (await _accounts.ResumeAsync(guest.SessionToken, CancellationToken.None)).IsGuest.Should().BeTrue();
    }

    [Fact]
    public async Task SetNicknameAsync_FirstChangeAfterSignIn_IsAllowedNow()
    {
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());
        string nickname = TestNicknames.Next();
        (await _transfers.ExecuteAsync([new TransferLeg(TransferParty.Player(signedIn.PlayerId), ItemCatalog.RenameScroll, 1)], "test.grant")).Should().Be(TransferStatus.Ok);

        NicknameOutcome outcome = await _accounts.SetNicknameAsync(signedIn.PlayerId, nickname, CancellationToken.None);

        outcome.Status.Should().Be(AccountStatus.Ok);
        outcome.Nickname.Should().Be(nickname);
    }

    [Fact]
    public async Task LogoutAsync_RevokesOnlyThatSession()
    {
        string subject = Unique();
        AccountOutcome first = await TestAccounts.SignInAsync(_accounts, subject);
        AccountOutcome second = await TestAccounts.SignInAsync(_accounts, subject);

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
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());

        (await lifecycle.UnlockAsync(signedIn.PlayerId, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task GuestCleanup_DeletesOnlyIdleGuestsWithTheirStateAndTokens()
    {
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        PlayerLifecycleService lifecycle = new(new EfUnitOfWorkFactory(_database.Options), _clock);
        AccountOutcome oldGuest = await _accounts.StartGuestAsync(GameLanguage.Unspecified, CancellationToken.None);
        AccountOutcome oldRegistered = await TestAccounts.SignInAsync(_accounts, Unique());
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
        AccountOutcome signedIn = await TestAccounts.SignInAsync(_accounts, Unique());
        PlayerStateService states = new(new EfUnitOfWorkFactory(_database.Options), _clock);

        PlayerStateRow? before = await states.LoadAsync(signedIn.PlayerId, CancellationToken.None);
        await states.SaveAsync(signedIn.PlayerId, new PlayerStateRow("Farm", 12.5, 40, Facing.Left), CancellationToken.None);
        await states.SaveAsync(signedIn.PlayerId, new PlayerStateRow("Town", 3, 4.25, Facing.Up), CancellationToken.None);
        PlayerStateRow? after = await states.LoadAsync(signedIn.PlayerId, CancellationToken.None);

        before.Should().BeNull();
        after.Should().Be(new PlayerStateRow("Town", 3, 4.25, Facing.Up));
    }

    private static string Unique() => Guid.NewGuid().ToString("N");
}
