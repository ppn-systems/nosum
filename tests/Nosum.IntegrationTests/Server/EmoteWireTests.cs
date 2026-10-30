// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Core;
using Nosum.Contracts.Emote;
using Nosum.Gameplay.Emotes;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Abstractions.Player;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The emote wheel over the wire (docs/adr/ADR-075 D1-D2): real Nalix WebSocket clients against the real host and PostgreSQL. The point is who
/// sees the bubble: the players on the sender's map, and not the players of another map. The cooldown and the code are checked on the wire.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class EmoteWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_silence = TimeSpan.FromMilliseconds(1_300);

    private readonly PostgresFixture _database;

    public EmoteWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task SendEmote_ShowsToThePlayersOnTheSameMap_AndNotToAnotherMap()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
        await PutOnAsync(carol, GameMaps.BusStop);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);

        (await a.SendEmoteAsync(0, host.Timeout)).Should().Be(EmoteStatus.Ok);

        (await b.Emotes.WaitAsync(host.Timeout)).Should().Be((a.MapPlayerId, (byte)0));
        await c.Emotes.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
        await a.Emotes.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
    }

    [Fact]
    public async Task SendEmote_SecondOneInsideTheCooldown_IsRefused()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        (await a.SendEmoteAsync(1, host.Timeout)).Should().Be(EmoteStatus.Ok);
        (await a.SendEmoteAsync(2, host.Timeout)).Should().Be(EmoteStatus.Cooldown, $"the cooldown is {EmoteRules.MinIntervalMs} ms");
    }

    [Fact]
    public async Task SendEmote_CodeOffTheWheel_IsRefused()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        (await a.SendEmoteAsync((byte)EmoteRules.Count, host.Timeout)).Should().Be(EmoteStatus.InvalidEmote);
    }

    private static async Task<Account> RegisterAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        AccountOutcome outcome = await TestAccounts.RegisterAsync(accounts, Guid.NewGuid().ToString("N"), TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, outcome.SessionToken, outcome.Nickname);
    }

    private static async Task PutOnAsync(Account account, string mapId)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(account.Id, new PlayerStateRow(mapId, -1, -1, Facing.Down), CancellationToken.None);
    }

    private sealed record Account(Guid Id, string Token, string Nickname);

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(180));

        private TestHost(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider? clock = null)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            NetworkApplication app = Program.BuildApplication(port, clock);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public Task<SocialLifeClient> ConnectAsync(Account account) => SocialLifeClient.ConnectAsync(this.Port, account.Token, this.Timeout);

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
