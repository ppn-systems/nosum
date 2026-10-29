// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Player;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Admin;
using Nosum.Contracts.Admin.C2S;
using Nosum.Contracts.Admin.S2C;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Common;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Domain.Social;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The admin intents over a real WebSocket against the real host and PostgreSQL (docs/adr/ADR-031 D7): the permission middleware keeps ordinary players
/// out, an admin account gets in, every effect shows on the target's side, and the logs of the whole run carry no secret (D5).
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class AdminWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_denied = TimeSpan.FromMilliseconds(1_500);

    private readonly PostgresFixture _database;

    public AdminWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Ban_BySomeoneWhoIsNotAnAdmin_GetsNoAnswerAndBansNobody()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account player = await RegisterAsync();
        Account target = await RegisterAsync();
        using WebSocketSession session = await host.SignInAsync(player);

        using AdminBan ban = new() { Nickname = target.Nickname };
        Func<Task> denied = async () => _ = await session.RequestAsync<AdminResult>(ban, RequestOptions.Default.WithEncrypt().WithTimeout((int)s_denied.TotalMilliseconds), ct: host.Timeout);

        _ = await denied.Should().ThrowAsync<Exception>();
        (await LoadAsync(target)).BannedAt.Should().BeNull();
    }

    [Fact]
    public async Task Ban_ByAnAdmin_EndsTheTargetsConnectionAndTheyCannotSignInAgain()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account admin = await RegisterAdminAsync();
        Account target = await RegisterAsync();
        using WebSocketSession adminSession = await host.SignInAsync(admin);
        using WebSocketSession targetSession = await host.SignInAsync(target);
        List<AccountStatus> ended = [];
        using IDisposable subscription = targetSession.On<SessionEnded>(p => ended.Add(p.Reason));

        using AdminBan ban = new() { Nickname = target.Nickname.ToUpperInvariant() };
        AdminResult result = await adminSession.RequestAsync<AdminResult>(ban, ct: host.Timeout);

        result.Status.Should().Be(AdminStatus.Ok);
        await WaitUntilAsync(() => ended.Count > 0, host.Timeout);
        ended.Should().Equal(AccountStatus.Banned);
        using WebSocketSession fresh = host.NewSession();
        await fresh.ConnectAsync();
        await fresh.HandshakeAsync();
        using ResumeSession resume = new() { SessionToken = target.Token };
        (await fresh.RequestAsync<AccountResult>(resume, ct: host.Timeout)).Status.Should().Be(AccountStatus.Banned);
    }

    [Fact]
    public async Task Kick_ByAnAdmin_TellsTheTargetWhyAndTheyMayComeBack()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account admin = await RegisterAdminAsync();
        Account target = await RegisterAsync();
        using WebSocketSession adminSession = await host.SignInAsync(admin);
        using WebSocketSession targetSession = await host.SignInAsync(target);
        List<AccountStatus> ended = [];
        using IDisposable subscription = targetSession.On<SessionEnded>(p => ended.Add(p.Reason));

        using AdminKick kick = new() { Nickname = target.Nickname };
        AdminResult result = await adminSession.RequestAsync<AdminResult>(kick, ct: host.Timeout);

        result.Status.Should().Be(AdminStatus.Ok);
        await WaitUntilAsync(() => ended.Count > 0, host.Timeout);
        ended.Should().Equal(AccountStatus.SessionKicked);
        using WebSocketSession again = await host.SignInAsync(target);
        (await LoadAsync(target)).BannedAt.Should().BeNull();
    }

    [Fact]
    public async Task Mute_ByAnAdmin_RefusesTheTargetsChatUntilItIsLifted()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account admin = await RegisterAdminAsync();
        Account target = await RegisterAsync();
        await PutOnAsync(target, GameMaps.Town);
        using WebSocketSession adminSession = await host.SignInAsync(admin);
        using WebSocketSession targetSession = await host.SignInAsync(target);
        await JoinAsync(targetSession, host.Timeout);

        using AdminMute mute = new() { Nickname = target.Nickname, Hours = 2 };
        (await adminSession.RequestAsync<AdminResult>(mute, ct: host.Timeout)).Status.Should().Be(AdminStatus.Ok);
        ChatStatus whileMuted = await ChatAsync(targetSession, "hello", host.Timeout);
        using AdminMute lift = new() { Nickname = target.Nickname, Hours = 0 };
        (await adminSession.RequestAsync<AdminResult>(lift, ct: host.Timeout)).Status.Should().Be(AdminStatus.Ok);
        ChatStatus afterLift = await ChatAsync(targetSession, "hello again", host.Timeout);

        whileMuted.Should().Be(ChatStatus.Muted);
        afterLift.Should().Be(ChatStatus.Ok);
    }

    [Fact]
    public async Task Reports_ByAnAdmin_AreListedAndReadable()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account admin = await RegisterAdminAsync();
        Account reporter = await RegisterAsync();
        Account reported = await RegisterAsync();
        Guid report = await AddReportAsync(reporter, reported, "first line", "second line");
        using WebSocketSession adminSession = await host.SignInAsync(admin);

        using AdminListReports list = new();
        AdminReportList reports = await adminSession.RequestAsync<AdminReportList>(list, ct: host.Timeout);
        using AdminReadReport read = new() { ReportId = report };
        AdminReportDetail detail = await adminSession.RequestAsync<AdminReportDetail>(read, ct: host.Timeout);

        reports.Status.Should().Be(AdminStatus.Ok);
        AdminReportRow row = reports.Reports.Single(r => r.ReportId == report);
        row.Reporter.Should().Be(reporter.Nickname);
        row.Reported.Should().Be(reported.Nickname);
        detail.Status.Should().Be(AdminStatus.Ok);
        detail.Lines.Select(l => l.Text).Should().Equal("first line", "second line");
    }

    [Fact]
    public async Task AdminRunThatBansMutesAndLogsIn_WritesLogsWithoutSecrets()
    {
        using StringWriter logs = new();
        OpsLog.Configure(logs);
        try
        {
            await using TestHost host = await TestHost.StartAsync(_database);
            Account admin = await RegisterAdminAsync();
            Account target = await RegisterAsync();
            await PutOnAsync(target, GameMaps.Town);
            using WebSocketSession adminSession = await host.SignInAsync(admin);
            using WebSocketSession targetSession = await host.SignInAsync(target);
            await JoinAsync(targetSession, host.Timeout);
            const string secretChat = "my private chat line zzqq";
            _ = await ChatAsync(targetSession, secretChat, host.Timeout);

            using AdminMute mute = new() { Nickname = target.Nickname, Hours = 1 };
            _ = await adminSession.RequestAsync<AdminResult>(mute, ct: host.Timeout);
            using AdminBan ban = new() { Nickname = target.Nickname };
            _ = await adminSession.RequestAsync<AdminResult>(ban, ct: host.Timeout);
            using WebSocketSession fresh = host.NewSession();
            await fresh.ConnectAsync();
            await fresh.HandshakeAsync();
            using ExternalSignIn login = new() { Provider = ExternalProvider.Google, IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, target.Subject) };
            _ = await fresh.RequestAsync<AccountResult>(login, ct: host.Timeout);

            string output = logs.ToString();
            output.Should().Contain("\"event\":\"admin.mute\"").And.Contain("\"event\":\"admin.ban\"");
            output.Should().Contain(admin.Id.ToString()).And.Contain(target.Id.ToString());
            foreach (string secret in new[] { admin.Subject, target.Subject, admin.Token, target.Token, FakeExternalTokenVerifier.Token(ExternalProvider.Google, target.Subject), secretChat, "gmail.com" })
            {
                output.Should().NotContain(secret);
            }
        }
        finally
        {
            OpsLog.Configure(TextWriter.Null);
        }
    }

    // ---- helpers ----

    private static async Task<ChatStatus> ChatAsync(WebSocketSession session, string text, CancellationToken ct)
    {
        using Nosum.Contracts.Chat.C2S.SendChat request = new() { Channel = ChatChannel.Local, Target = string.Empty, Text = text };
        return (await session.RequestAsync<Nosum.Contracts.Chat.S2C.ChatResult>(request, ct: ct)).Status;
    }

    private static async Task JoinAsync(WebSocketSession session, CancellationToken ct)
    {
        using Nosum.Contracts.Player.C2S.JoinMap join = new() { MapId = Program.FarmMapId };
        Nosum.Contracts.Player.S2C.JoinMapResult result = await session.RequestAsync<Nosum.Contracts.Player.S2C.JoinMapResult>(join, ct: ct);
        result.Status.Should().Be(Nosum.Contracts.Player.JoinStatus.Ok);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(25, ct);
        }
    }

    private async Task<Player> LoadAsync(Account account)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.Players.AsNoTracking().SingleAsync(p => p.Id == account.Id);
    }

    private async Task<Guid> AddReportAsync(Account reporter, Account reported, params string[] texts)
    {
        await using NosumDbContext db = new(_database.Options);
        ChatReport report = new() { ReporterId = reporter.Id, ReportedId = reported.Id, CreatedAt = DateTimeOffset.UtcNow };
        db.ChatReports.Add(report);
        for (int i = 0; i < texts.Length; i++)
        {
            db.ChatReportLines.Add(new ChatReportLine { ReportId = report.Id, Sequence = i, Channel = 0, SenderId = reported.Id, SenderNickname = reported.Nickname, Text = texts[i], SentAt = DateTimeOffset.UtcNow });
        }

        _ = await db.SaveChangesAsync();
        return report.Id;
    }

    private async Task<Account> RegisterAdminAsync()
    {
        Account account = await RegisterAsync();
        await using NosumDbContext db = new(_database.Options);
        _ = await db.Players.Where(p => p.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Role, PlayerRole.Admin));
        return account;
    }

    private static async Task<Account> RegisterAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        string subject = Guid.NewGuid().ToString("N");
        AccountOutcome outcome = await TestAccounts.RegisterAsync(accounts, subject, TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, subject, outcome.SessionToken, outcome.Nickname);
    }

    private static async Task PutOnAsync(Account account, string mapId)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(account.Id, new PlayerStateRow(mapId, -1, -1, Facing.Down), CancellationToken.None);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed record Account(Guid Id, string Subject, string Token, string Nickname);

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(120));
        private readonly ushort _port;

        private TestHost(NetworkApplication app, ushort port)
        {
            _app = app;
            _port = port;
        }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            NetworkApplication app = Program.BuildApplication(port);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public WebSocketSession NewSession()
            => new(
                new TransportOptions { Address = "127.0.0.1", Port = _port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });

        // Resuming a session is limited to two a second per address.
        public async Task<WebSocketSession> SignInAsync(Account account)
        {
            WebSocketSession session = this.NewSession();
            await session.ConnectAsync(ct: this.Timeout);
            await session.HandshakeAsync(this.Timeout);
            await Task.Delay(700, this.Timeout);
            using ResumeSession resume = new() { SessionToken = account.Token };
            (await session.RequestAsync<AccountResult>(resume, ct: this.Timeout)).Status.Should().Be(AccountStatus.Ok);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
