// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Sign-in, link, resume and logout over a real WebSocket against the real host and a real PostgreSQL database.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class AccountWireTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;
    private ushort _port;

    public AccountWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task ExternalSignInThenResume_OverWebSocket_SignsTheSamePlayerInEachTime()
    {
        await using NetworkApplication app = await StartAsync();
        string subject = NewSubject();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await WirePace.CredentialAsync(timeout.Token);
            AccountResult registered = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                NewSignIn(subject, GameLanguage.English), ct: timeout.Token));
            await WirePace.CredentialAsync(timeout.Token);
            AccountResult signedInAgain = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                NewSignIn(subject, GameLanguage.English), ct: timeout.Token));
            AccountResult resumed = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = registered.SessionToken }, ct: timeout.Token));

            registered.Status.Should().Be(AccountStatus.Ok);
            registered.PreferredLanguage.Should().Be(GameLanguage.English);
            registered.IsGuest.Should().BeFalse();
            signedInAgain.Status.Should().Be(AccountStatus.Ok);
            signedInAgain.PlayerId.Should().Be(registered.PlayerId);
            signedInAgain.SessionToken.Should().NotBeEmpty();
            resumed.Status.Should().Be(AccountStatus.Ok);
            resumed.PlayerId.Should().Be(registered.PlayerId);
            resumed.Nickname.Should().Be(registered.Nickname);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExternalSignIn_TokenTheVerifierRejects_RepliesInvalidIdTokenWithoutSession()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await WirePace.CredentialAsync(timeout.Token);
            AccountResult result = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ExternalSignIn { Provider = ExternalProvider.Google, IdToken = "not-a-real-token" }, ct: timeout.Token));

            result.Status.Should().Be(AccountStatus.InvalidIdToken);
            result.SessionToken.Should().BeEmpty();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SetLanguage_BeforeSignIn_IsDeniedAndAfterSignInIsStored()
    {
        await using NetworkApplication app = await StartAsync();
        string subject = NewSubject();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            // Same connection: the permission level rises only after sign-in succeeds.
            using WebSocketSession session = NewSession();
            await session.ConnectAsync();
            await session.HandshakeAsync();

            Func<Task> denied = async () => _ = await session.RequestAsync<LanguageResult>(
                new SetLanguage { Language = GameLanguage.English }, RequestOptions.Default.WithTimeout(1500), ct: timeout.Token);
            _ = await denied.Should().ThrowAsync<Exception>();

            using ExternalSignIn signIn = NewSignIn(subject, GameLanguage.Unspecified);
            await WirePace.CredentialAsync(timeout.Token);
            AccountResult signedIn = await session.RequestAsync<AccountResult>(signIn, ct: timeout.Token);
            using SetLanguage setLanguage = new() { Language = GameLanguage.English };
            LanguageResult allowed = await session.RequestAsync<LanguageResult>(setLanguage, ct: timeout.Token);

            signedIn.PreferredLanguage.Should().Be(GameLanguage.Vietnamese);
            allowed.Status.Should().Be(AccountStatus.Ok);
            allowed.Language.Should().Be(GameLanguage.English);
            AccountResult next = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                NewSignIn(subject, GameLanguage.Unspecified), ct: timeout.Token));
            next.PreferredLanguage.Should().Be(GameLanguage.English);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GuestThenLink_OverWebSocket_KeepsThePlayerAndRevokesTheGuestToken()
    {
        await using NetworkApplication app = await StartAsync();
        string subject = NewSubject();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            using WebSocketSession session = NewSession();
            await session.ConnectAsync();
            await session.HandshakeAsync();

            using StartGuest start = new() { PreferredLanguage = GameLanguage.English };
            AccountResult guest = await session.RequestAsync<AccountResult>(start, ct: timeout.Token);
            using LinkExternalIdentity link = NewLink(subject, TestNicknames.Next());
            await WirePace.CredentialAsync(timeout.Token);
            AccountResult linked = await session.RequestAsync<AccountResult>(link, ct: timeout.Token);
            using LinkExternalIdentity again = NewLink(NewSubject(), TestNicknames.Next());
            AccountResult second = await session.RequestAsync<AccountResult>(again, ct: timeout.Token);
            AccountResult guestToken = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = guest.SessionToken }, ct: timeout.Token));
            AccountResult newToken = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = linked.SessionToken }, ct: timeout.Token));

            guest.IsGuest.Should().BeTrue();
            guest.Email.Should().BeEmpty();
            linked.Status.Should().Be(AccountStatus.Ok);
            linked.PlayerId.Should().Be(guest.PlayerId);
            linked.IsGuest.Should().BeFalse();
            second.Status.Should().Be(AccountStatus.NotGuest);
            guestToken.Status.Should().Be(AccountStatus.InvalidToken);
            newToken.PlayerId.Should().Be(guest.PlayerId);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Logout_OverWebSocket_RevokesTheTokenAndSignsTheConnectionOut()
    {
        await using NetworkApplication app = await StartAsync();
        string subject = NewSubject();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            using WebSocketSession session = NewSession();
            await session.ConnectAsync();
            await session.HandshakeAsync();
            using ExternalSignIn signIn = NewSignIn(subject, GameLanguage.Unspecified);
            await WirePace.CredentialAsync(timeout.Token);
            AccountResult signedIn = await session.RequestAsync<AccountResult>(signIn, ct: timeout.Token);

            using Logout logout = new();
            AccountResult loggedOut = await session.RequestAsync<AccountResult>(logout, ct: timeout.Token);
            AccountResult resumed = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = signedIn.SessionToken }, ct: timeout.Token));
            using SetLanguage setLanguage = new() { Language = GameLanguage.English };
            Func<Task> denied = async () => _ = await session.RequestAsync<LanguageResult>(
                setLanguage, RequestOptions.Default.WithTimeout(1500), ct: timeout.Token);

            loggedOut.Status.Should().Be(AccountStatus.Ok);
            resumed.Status.Should().Be(AccountStatus.InvalidToken);
            _ = await denied.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private static string NewSubject() => Guid.NewGuid().ToString("N");

    private static ExternalSignIn NewSignIn(string subject, GameLanguage language) => new()
    {
        Provider = ExternalProvider.Google,
        IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, subject),
        PreferredLanguage = language,
    };

    private static LinkExternalIdentity NewLink(string subject, string nickname) => new()
    {
        Provider = ExternalProvider.Google,
        IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, subject),
        Nickname = nickname,
    };

    private async Task<NetworkApplication> StartAsync()
    {
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        _port = GetFreePort();
        NetworkApplication app = Program.BuildApplication(_port);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        await app.ActivateAsync(timeout.Token);
        return app;
    }

    private WebSocketSession NewSession()
        => new(
            new TransportOptions { Address = "127.0.0.1", Port = _port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });

    private async Task<T> WithSessionAsync<T>(NetworkApplication app, Func<WebSocketSession, ValueTask<T>> action)
    {
        using WebSocketSession session = NewSession();
        await session.ConnectAsync();
        await session.HandshakeAsync();
        return await action(session);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
