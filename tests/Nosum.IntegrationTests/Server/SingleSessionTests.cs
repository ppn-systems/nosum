// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>One active connection per player, over real WebSockets, the real host and a real PostgreSQL database.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class SingleSessionTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _database;

    public SingleSessionTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task SecondLogin_EndsTheFirstConnection_AndItsPositionIsSavedOnceAndFollowedByTheNewOne()
    {
        string email = "s" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        CountingStates states = new(InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!);
        InstanceManager.Instance.Register<IPlayerStateService>(states);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            using WebSocketSession first = await ConnectAsync(port, timeout.Token);
            BlockingCollection<AccountStatus> ended = [];
            using IDisposable subscription = first.On<SessionEnded>(p => ended.Add(p.Reason));

            using Register register = new() { Email = email, Password = "password-1" };
            AccountResult registered = await first.RequestAsync<AccountResult>(register, ct: timeout.Token);
            using JoinMap join = new() { MapId = Program.FarmMapId };
            JoinMapResult joined = await first.RequestAsync<JoinMapResult>(join, ct: timeout.Token);
            float targetX = joined.SpawnX + 3;
            using MoveIntent move = new() { Sequence = 1, X = targetX, Y = joined.SpawnY, Facing = 1, Moving = false };
            await first.SendAsync(move, timeout.Token);
            await WaitUntilAsync(() => PlayerAt(registered.PlayerId, targetX), timeout.Token);

            using WebSocketSession second = await ConnectAsync(port, timeout.Token);
            using Login login = new() { Email = email, Password = "password-1" };
            AccountResult relogin = await second.RequestAsync<AccountResult>(login, ct: timeout.Token);
            using JoinMap joinAgain = new() { MapId = Program.FarmMapId };
            JoinMapResult rejoined = await second.RequestAsync<JoinMapResult>(joinAgain, ct: timeout.Token);

            relogin.Status.Should().Be(AccountStatus.Ok);
            relogin.PlayerId.Should().Be(registered.PlayerId);
            ended.Take(timeout.Token).Should().Be(AccountStatus.SessionReplaced);
            await WaitUntilAsync(() => !first.IsConnected, timeout.Token);
            states.SavesOf(registered.PlayerId).Should().Be(1);
            rejoined.Status.Should().Be(JoinStatus.Ok);
            rejoined.SpawnX.Should().BeApproximately(targetX, 0.01f);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task GuestResume_OnANewConnection_EndsTheOlderGuestConnection()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            using WebSocketSession first = await ConnectAsync(port, timeout.Token);
            BlockingCollection<AccountStatus> ended = [];
            using IDisposable subscription = first.On<SessionEnded>(p => ended.Add(p.Reason));
            using StartGuest start = new();
            AccountResult guest = await first.RequestAsync<AccountResult>(start, ct: timeout.Token);

            using WebSocketSession second = await ConnectAsync(port, timeout.Token);
            using ResumeSession resume = new() { SessionToken = guest.SessionToken };
            AccountResult resumed = await second.RequestAsync<AccountResult>(resume, ct: timeout.Token);

            resumed.PlayerId.Should().Be(guest.PlayerId);
            ended.Take(timeout.Token).Should().Be(AccountStatus.SessionReplaced);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AccountPackets_SentWithoutHandshake_AreRefused()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            using WebSocketSession plain = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            await plain.ConnectAsync(ct: timeout.Token);
            using StartGuest start = new();

            Func<Task> act = async () => _ = await plain.RequestAsync<AccountResult>(
                start, RequestOptions.Default.WithTimeout(1500), ct: timeout.Token);

            _ = await act.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private static bool PlayerAt(Guid accountId, float x)
    {
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        List<PlayerPresence> players = [];
        presence.CopyPlayers(MapInstance.Of(Program.FarmMapId, accountId).Key, players);
        foreach (PlayerPresence player in players)
        {
            lock (player.Gate)
            {
                if (Math.Abs(player.Motion.X - x) < 0.01)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct);
        }
    }

    private static async Task<WebSocketSession> ConnectAsync(ushort port, CancellationToken ct)
    {
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        await session.ConnectAsync(ct: ct);
        await session.HandshakeAsync(ct);
        return session;
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class CountingStates : IPlayerStateService
    {
        private readonly IPlayerStateService _inner;
        private readonly ConcurrentDictionary<Guid, int> _saves = new();

        public CountingStates(IPlayerStateService inner) => _inner = inner;

        public int SavesOf(Guid playerId) => _saves.GetValueOrDefault(playerId);

        public Task<PlayerStateRow?> LoadAsync(Guid playerId, CancellationToken cancellationToken)
            => _inner.LoadAsync(playerId, cancellationToken);

        public Task SaveAsync(Guid playerId, PlayerStateRow state, CancellationToken cancellationToken)
        {
            _ = _saves.AddOrUpdate(playerId, 1, static (_, n) => n + 1);
            return _inner.SaveAsync(playerId, state, cancellationToken);
        }
    }
}
