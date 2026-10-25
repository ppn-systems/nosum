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

/// <summary>Register, login and resume over a real WebSocket against the real host and a real PostgreSQL database.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class AccountWireTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;
    private ushort _port;

    public AccountWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task RegisterThenLoginThenResume_OverWebSocket_SignsTheSamePlayerInEachTime()
    {
        await using NetworkApplication app = await StartAsync();
        string name = "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            AccountResult registered = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new Register { Email = name, Password = "password-1", PreferredLanguage = GameLanguage.English }, ct: timeout.Token));
            AccountResult loggedIn = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new Login { Email = name, Password = "password-1" }, ct: timeout.Token));
            AccountResult resumed = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = registered.SessionToken }, ct: timeout.Token));

            registered.Status.Should().Be(AccountStatus.Ok);
            registered.PreferredLanguage.Should().Be(GameLanguage.English);
            loggedIn.Status.Should().Be(AccountStatus.Ok);
            loggedIn.PlayerId.Should().Be(registered.PlayerId);
            loggedIn.SessionToken.Should().NotBeEmpty();
            resumed.Status.Should().Be(AccountStatus.Ok);
            resumed.PlayerId.Should().Be(registered.PlayerId);
            resumed.Email.Should().Be(name);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Login_WrongPassword_RepliesInvalidCredentialsCodeWithoutSession()
    {
        await using NetworkApplication app = await StartAsync();
        string name = "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            _ = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new Register { Email = name, Password = "password-1" }, ct: timeout.Token));

            AccountResult result = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new Login { Email = name, Password = "wrong-password" }, ct: timeout.Token));

            result.Status.Should().Be(AccountStatus.InvalidCredentials);
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
        string name = "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            // Same connection: the permission level rises only after register succeeds.
            using WebSocketSession session = NewSession();
            await session.ConnectAsync();
        await session.HandshakeAsync();

            Func<Task> denied = async () => _ = await session.RequestAsync<LanguageResult>(
                new SetLanguage { Language = GameLanguage.English }, RequestOptions.Default.WithTimeout(1500), ct: timeout.Token);
            _ = await denied.Should().ThrowAsync<Exception>();

            using Register register = new() { Email = name, Password = "password-1" };
            AccountResult registered = await session.RequestAsync<AccountResult>(register, ct: timeout.Token);
            using SetLanguage setLanguage = new() { Language = GameLanguage.English };
            LanguageResult allowed = await session.RequestAsync<LanguageResult>(setLanguage, ct: timeout.Token);

            registered.PreferredLanguage.Should().Be(GameLanguage.Vietnamese);
            allowed.Status.Should().Be(AccountStatus.Ok);
            allowed.Language.Should().Be(GameLanguage.English);
            AccountResult next = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new Login { Email = name, Password = "password-1" }, ct: timeout.Token));
            next.PreferredLanguage.Should().Be(GameLanguage.English);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GuestThenUpgrade_OverWebSocket_KeepsThePlayerAndRevokesTheGuestToken()
    {
        await using NetworkApplication app = await StartAsync();
        string email = "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            using WebSocketSession session = NewSession();
            await session.ConnectAsync();
        await session.HandshakeAsync();

            using StartGuest start = new() { PreferredLanguage = GameLanguage.English };
            AccountResult guest = await session.RequestAsync<AccountResult>(start, ct: timeout.Token);
            using UpgradeGuest upgrade = new() { Email = email, Password = "password-1" };
            AccountResult upgraded = await session.RequestAsync<AccountResult>(upgrade, ct: timeout.Token);
            using UpgradeGuest again = new() { Email = "x" + email, Password = "password-1" };
            AccountResult second = await session.RequestAsync<AccountResult>(again, ct: timeout.Token);
            AccountResult guestToken = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = guest.SessionToken }, ct: timeout.Token));
            AccountResult newToken = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = upgraded.SessionToken }, ct: timeout.Token));

            guest.IsGuest.Should().BeTrue();
            guest.Email.Should().BeEmpty();
            upgraded.Status.Should().Be(AccountStatus.Ok);
            upgraded.PlayerId.Should().Be(guest.PlayerId);
            upgraded.IsGuest.Should().BeFalse();
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
        string email = "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            using WebSocketSession session = NewSession();
            await session.ConnectAsync();
        await session.HandshakeAsync();
            using Register register = new() { Email = email, Password = "password-1" };
            AccountResult registered = await session.RequestAsync<AccountResult>(register, ct: timeout.Token);

            using Logout logout = new();
            AccountResult loggedOut = await session.RequestAsync<AccountResult>(logout, ct: timeout.Token);
            AccountResult resumed = await WithSessionAsync(app, s => s.RequestAsync<AccountResult>(
                new ResumeSession { SessionToken = registered.SessionToken }, ct: timeout.Token));
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

    private async Task<NetworkApplication> StartAsync()
    {
        HostServices.Register(_database.Options);
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
