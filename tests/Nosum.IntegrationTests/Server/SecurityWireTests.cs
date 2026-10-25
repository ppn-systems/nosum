// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Abstractions;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.Runtime.Handlers;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Npgsql;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Email;
using Nosum.Abstractions.Security;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Handlers.Account;
using Nosum.Handlers.Security;
using Nosum.Host;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>ADR-032 over a real WebSocket against the real host: bounds, unauthenticated access, recovery, session hardening, abuse caps and key pinning.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class SecurityWireTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;
    private readonly RecordingSender _mail = new();
    private ushort _port;

    public SecurityWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task OversizedAndMalformedFields_AreDroppedBeforeTheHandler_AndTheConnectionStaysUsable()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            using WebSocketSession session = await ConnectAsync();
            long before = InputBoundsMiddleware.Dropped;
            using Login oversized = new() { Email = new string('a', 5_000) + "@gmail.com", Password = "password-1" };
            using Register badChest = new() { Email = Unique(), Password = "password-1", Nickname = new string('n', 500) };

            Func<Task> first = async () => _ = await session.RequestAsync<AccountResult>(oversized, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: timeout.Token);
            Func<Task> second = async () => _ = await session.RequestAsync<AccountResult>(badChest, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: timeout.Token);
            _ = await first.Should().ThrowAsync<Exception>("an oversized packet gets no reply");
            _ = await second.Should().ThrowAsync<Exception>("an oversized nickname gets no reply");

            InputBoundsMiddleware.Dropped.Should().Be(before + 2);
            using Register fine = new() { Email = Unique(), Password = "password-1", Nickname = TestNicknames.Next() };
            AccountResult ok = await session.RequestAsync<AccountResult>(fine, ct: timeout.Token);
            ok.Status.Should().Be(AccountStatus.Ok);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EveryClientPacket_SentWithoutSigningIn_ChangesNothingAndRepliesOnlyWithTheDocumentedStatus()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
        try
        {
            Type[] packets = typeof(AccountOpCode).Assembly.GetTypes()
                .Where(t => t.Namespace is not null && t.Namespace.EndsWith(".C2S", StringComparison.Ordinal) && t is { IsClass: true, IsAbstract: false })
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToArray();
            HashSet<Type> publicPackets = [typeof(Register), typeof(Login), typeof(ResumeSession), typeof(StartGuest), typeof(RequestPasswordReset), typeof(ConfirmPasswordReset)];
            Dictionary<string, long> rowsBefore = await CountRowsAsync();

            int replies = 0;
            string current = string.Empty;
            System.Collections.Concurrent.ConcurrentBag<string> repliedTo = [];
            int reconnects = 0;
            foreach (bool encrypted in new[] { true, false })
            {
                WebSocketSession? session = null;
                try
                {
                    foreach (Type type in packets.Where(t => !publicPackets.Contains(t)))
                    {
                        if (session is null || !session.IsConnected)
                        {
                            session?.Dispose();
                            session = await ConnectAsync();
                            session.OnMessageReceived += (_, lease) =>
                            {
                                // Opcodes below 0x0100 belong to Nalix itself (it may close a connection that keeps sending denied packets); anything above would be a game reply.
                                if (lease.Length >= 2 && (lease.Span[0] | (lease.Span[1] << 8)) >= 0x0100)
                                {
                                    _ = Interlocked.Increment(ref replies);
                                    repliedTo.Add(current + " (" + encrypted + ") " + Convert.ToHexString(lease.Span[..Math.Min(16, lease.Length)]));
                                }

                                lease.Dispose();
                            };
                            reconnects++;
                        }

                        current = type.Name;
                        object instance = Activator.CreateInstance(type)!;
                        using IDisposable owner = (IDisposable)instance;
                        await session.SendAsync((IPacket)instance, encrypted, timeout.Token);
                        await Task.Delay(15, timeout.Token);
                    }
                }
                finally
                {
                    session?.Dispose();
                }
            }

            reconnects.Should().BeLessThan(10, "denied packets must not cost the connection");
            await Task.Delay(500, timeout.Token);
            replies.Should().Be(0, "a connection that is not signed in must get no reply to any signed-in packet, but got one to {0}", string.Join(", ", repliedTo));
            (await CountRowsAsync()).Should().BeEquivalentTo(rowsBefore);

            // The public ones answer with a documented refusal and still change nothing.
            using WebSocketSession open = await ConnectAsync();
            using Login login = new();
            using Register register = new();
            using ResumeSession resume = new();
            using RequestPasswordReset request = new();
            using ConfirmPasswordReset confirm = new();
            (await open.RequestAsync<AccountResult>(login, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidCredentials);
            (await open.RequestAsync<AccountResult>(register, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidEmail);
            (await open.RequestAsync<AccountResult>(resume, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidToken);
            (await open.RequestAsync<PasswordResult>(request, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidEmail);
            (await open.RequestAsync<PasswordResult>(confirm, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidResetCode);
            (await CountRowsAsync()).Should().BeEquivalentTo(rowsBefore);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PasswordRecovery_OverWebSocket_ResetsThePasswordAndEndsTheLiveSession()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            string email = Unique();
            using WebSocketSession live = await ConnectAsync();
            using Register register = new() { Email = email, Password = "password-1", Nickname = TestNicknames.Next() };
            AccountResult registered = await live.RequestAsync<AccountResult>(register, ct: timeout.Token);
            TaskCompletionSource<AccountStatus> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = live.On<SessionEnded>(p => ended.TrySetResult(p.Reason));

            using WebSocketSession forgotten = await ConnectAsync();
            using RequestPasswordReset request = new() { Email = email };
            PasswordResult requested = await forgotten.RequestAsync<PasswordResult>(request, ct: timeout.Token);
            requested.Status.Should().Be(AccountStatus.Ok);
            string code = _mail.Sent.Should().ContainSingle().Subject.Code;

            string wrong = code == "00000000" ? "11111111" : "00000000";
            using ConfirmPasswordReset bad = new() { Email = email, Code = wrong, NewPassword = "new-password-2" };
            (await forgotten.RequestAsync<PasswordResult>(bad, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidResetCode);
            using ConfirmPasswordReset good = new() { Email = email, Code = code, NewPassword = "new-password-2" };
            (await forgotten.RequestAsync<PasswordResult>(good, ct: timeout.Token)).Status.Should().Be(AccountStatus.Ok);

            AccountStatus reason = await ended.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
            reason.Should().Be(AccountStatus.SessionRevoked);
            using Login oldPassword = new() { Email = email, Password = "password-1" };
            using Login newPassword = new() { Email = email, Password = "new-password-2" };
            (await forgotten.RequestAsync<AccountResult>(oldPassword, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidCredentials);
            (await forgotten.RequestAsync<AccountResult>(newPassword, ct: timeout.Token)).Status.Should().Be(AccountStatus.Ok);
            using ResumeSession stale = new() { SessionToken = registered.SessionToken };
            (await forgotten.RequestAsync<AccountResult>(stale, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidToken);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ChangePassword_OverWebSocket_KeepsTheCallersSessionAndRevokesTheOthers()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            string email = Unique();
            using WebSocketSession a = await ConnectAsync();
            using Register register = new() { Email = email, Password = "password-1", Nickname = TestNicknames.Next() };
            AccountResult first = await a.RequestAsync<AccountResult>(register, ct: timeout.Token);
            using WebSocketSession b = await ConnectAsync();
            using Login login = new() { Email = email, Password = "password-1" };
            AccountResult second = await b.RequestAsync<AccountResult>(login, ct: timeout.Token);

            using ChangePassword wrong = new() { CurrentPassword = "nope-nope-1", NewPassword = "new-password-2" };
            (await b.RequestAsync<PasswordResult>(wrong, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidCredentials);
            using ChangePassword change = new() { CurrentPassword = "password-1", NewPassword = "new-password-2" };
            (await b.RequestAsync<PasswordResult>(change, ct: timeout.Token)).Status.Should().Be(AccountStatus.Ok);

            using WebSocketSession c = await ConnectAsync();
            using ResumeSession firstToken = new() { SessionToken = first.SessionToken };
            using ResumeSession secondToken = new() { SessionToken = second.SessionToken };
            (await c.RequestAsync<AccountResult>(firstToken, ct: timeout.Token)).Status.Should().Be(AccountStatus.InvalidToken);
            (await c.RequestAsync<AccountResult>(secondToken, ct: timeout.Token)).Status.Should().Be(AccountStatus.Ok);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ResumeSession_OverWebSocket_HandsOutAFreshTokenEachTime()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            using WebSocketSession a = await ConnectAsync();
            using StartGuest start = new() { PreferredLanguage = GameLanguage.English };
            AccountResult guest = await a.RequestAsync<AccountResult>(start, ct: timeout.Token);

            using WebSocketSession b = await ConnectAsync();
            using ResumeSession resume = new() { SessionToken = guest.SessionToken };
            AccountResult resumed = await b.RequestAsync<AccountResult>(resume, ct: timeout.Token);

            resumed.Status.Should().Be(AccountStatus.Ok);
            resumed.PlayerId.Should().Be(guest.PlayerId);
            resumed.SessionToken.Should().NotBeEmpty().And.NotBe(guest.SessionToken);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task NewAccounts_BeyondTheDailyCapPerAddress_AreRefused()
    {
        await using NetworkApplication app = await StartAsync(enforceLoopback: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
        try
        {
            AccountHandlers.NewAccountsPerAddressPerDay = 2;
            using WebSocketSession session = await ConnectAsync();
            for (int i = 0; i < 2; i++)
            {
                using StartGuest start = new() { PreferredLanguage = GameLanguage.English };
                (await session.RequestAsync<AccountResult>(start, ct: timeout.Token)).Status.Should().Be(AccountStatus.Ok, "guest {0} is within the cap", i);
            }

            using StartGuest over = new() { PreferredLanguage = GameLanguage.English };
            (await session.RequestAsync<AccountResult>(over, ct: timeout.Token)).Status.Should().Be(AccountStatus.TooManyRequests);
            using Register register = new() { Email = Unique(), Password = "password-1", Nickname = TestNicknames.Next() };
            (await session.RequestAsync<AccountResult>(register, ct: timeout.Token)).Status.Should().Be(AccountStatus.TooManyRequests);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Handshake_WithThePinnedServerKey_Succeeds_AndWithAnotherKeyIsRefused()
    {
        await using NetworkApplication app = await StartAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            string pin = HandshakeHandlers.ServerPublicKey.ToString();
            byte[] other = HandshakeHandlers.ServerPublicKey.ToByteArray();
            other[0] ^= 0xFF;
            string wrongPin = Convert.ToHexString(other);

            using WebSocketSession pinned = NewSession(pin);
            await pinned.ConnectAsync();
            await pinned.HandshakeAsync();
            using StartGuest start = new() { PreferredLanguage = GameLanguage.English };
            (await pinned.RequestAsync<AccountResult>(start, ct: timeout.Token)).Status.Should().Be(AccountStatus.Ok);

            using WebSocketSession impostor = NewSession(wrongPin);
            await impostor.ConnectAsync();
            Func<Task> handshake = async () => await impostor.HandshakeAsync();
            _ = await handshake.Should().ThrowAsync<Exception>("a server presenting another key must be refused");
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private async Task<NetworkApplication> StartAsync(bool enforceLoopback = false)
    {
        HostServices.Register(_database.Options);
        // The tests read the code from a recording sender instead of an inbox.
        InstanceManager.Instance.Register<IAccountService>(new AccountService(
            new EfUnitOfWorkFactory(_database.Options), new Pbkdf2PasswordHasher(iterations: 1000), new SessionTokenService(), TimeProvider.System,
            nicknames: InstanceManager.Instance.GetExistingInstance<INicknameDirectory>(), email: _mail));
        InstanceManager.Instance.Register<IAbuseGuard>(new SlidingWindowAbuseGuard(TimeProvider.System, exemptLoopback: !enforceLoopback));
        _port = GetFreePort();
        NetworkApplication app = Program.BuildApplication(_port);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        await app.ActivateAsync(timeout.Token);
        return app;
    }

    private WebSocketSession NewSession(string? serverPublicKey = null)
        => new(
            new TransportOptions { Address = "127.0.0.1", Port = _port, ServerPublicKey = serverPublicKey, AllowTofu = serverPublicKey is null },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });

    private async Task<WebSocketSession> ConnectAsync()
    {
        WebSocketSession session = NewSession();
        await session.ConnectAsync();
        await session.HandshakeAsync();
        return session;
    }

    private async Task<Dictionary<string, long>> CountRowsAsync()
    {
        await using NpgsqlConnection connection = new(_database.ConnectionString);
        await connection.OpenAsync();
        List<string> tables = [];
        await using (NpgsqlCommand list = new("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", connection))
        await using (NpgsqlDataReader reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Dictionary<string, long> counts = [];
        foreach (string table in tables)
        {
#pragma warning disable CA2100 // Table names come from information_schema, not from input.
            await using NpgsqlCommand count = new("SELECT count(*) FROM \"" + table + "\"", connection);
#pragma warning restore CA2100
            counts[table] = (long)(await count.ExecuteScalarAsync())!;
        }

        return counts;
    }

    private static string Unique() => "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            lock (this.Sent)
            {
                this.Sent.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
