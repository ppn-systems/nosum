// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Concurrent;
using System.Linq;
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

/// <summary>Two real Nalix WebSocket clients (registered accounts) against the real host and a real PostgreSQL database.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class MultiplayerMovementTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public MultiplayerMovementTests(PostgresFixture database) => _database = database;

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task MoveIntent_FromPlayerA_ReachesPlayerBInSnapshots()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using TestClient a = await TestClient.ConnectAsync(port, timeout.Token);
            await using TestClient b = await TestClient.ConnectAsync(port, timeout.Token);

            // The farm is private per player; the bus stop is where two players meet (docs/adr/ADR-015).
            await a.PutOnAsync(GameMaps.BusStop, timeout.Token);
            await b.PutOnAsync(GameMaps.BusStop, timeout.Token);
            JoinMapResult joinA = await a.JoinAsync(timeout.Token);
            JoinMapResult joinB = await b.JoinAsync(timeout.Token);
            joinA.Status.Should().Be(JoinStatus.Ok);
            joinB.Status.Should().Be(JoinStatus.Ok);
            joinA.PlayerId.Should().NotBe(joinB.PlayerId);

            float targetX = joinA.SpawnX + 3;
            await a.SendMoveAsync(1, targetX, joinA.SpawnY, timeout.Token);

            PlayerState seen = await b.WaitForPlayerAsync(joinA.PlayerId, p => Math.Abs(p.X - targetX) < 0.01f, timeout.Token);
            seen.Facing.Should().Be(1);
            seen.Moving.Should().BeTrue();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MoveIntent_Teleport_IsRefusedWithCorrection()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using TestClient a = await TestClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await a.JoinAsync(timeout.Token);

            await a.SendMoveAsync(7, join.SpawnX - 100, join.SpawnY, timeout.Token);

            MoveCorrection correction = await a.Corrections.WaitAsync(timeout.Token);
            correction.Sequence.Should().Be(7);
            correction.Reason.Should().Be(MoveRefusal.TooFast);
            correction.X.Should().BeApproximately(join.SpawnX, 0.01f);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Disconnect_PlayerA_TellsPlayerBThatAIsGone()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using TestClient b = await TestClient.ConnectAsync(port, timeout.Token);
            await b.PutOnAsync(GameMaps.BusStop, timeout.Token);
            await b.JoinAsync(timeout.Token);
            uint idOfA;
            await using (TestClient a = await TestClient.ConnectAsync(port, timeout.Token))
            {
                await a.PutOnAsync(GameMaps.BusStop, timeout.Token);
                idOfA = (await a.JoinAsync(timeout.Token)).PlayerId;
                await b.WaitForPlayerAsync(idOfA, _ => true, timeout.Token);
            }

            PlayerLeft left = await b.Left.WaitAsync(timeout.Token);
            left.PlayerId.Should().Be(idOfA);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task JoinMap_BeforeSignIn_IsNotAnswered()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            using WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            await session.ConnectAsync(ct: timeout.Token);
            await session.HandshakeAsync(timeout.Token);
            using JoinMap request = new() { MapId = Program.FarmMapId };

            Func<Task> join = async () => _ = await session.RequestAsync<JoinMapResult>(request, RequestOptions.Default.WithTimeout(1500), ct: timeout.Token);

            _ = await join.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task JoinMap_AfterLeavingFromAWalkedPosition_ReturnsToThatPosition()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            string sessionToken;
            Guid accountId;
            float targetX;
            await using (TestClient first = await TestClient.ConnectAsync(port, timeout.Token))
            {
                sessionToken = first.SessionToken;
                accountId = first.AccountId;
                JoinMapResult join = await first.JoinAsync(timeout.Token);
                targetX = join.SpawnX + 3;
                await first.SendMoveAsync(1, targetX, join.SpawnY, timeout.Token);
                // Snapshots include the receiver, so its own entry shows the server accepted the move.
                await first.WaitForPlayerAsync(join.PlayerId, p => Math.Abs(p.X - targetX) < 0.01f, timeout.Token);
            }

            // The save happens when the server notices the disconnect.
            IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
            PlayerStateRow? saved = null;
            while (saved is null || Math.Abs(saved.Value.X - targetX) >= 0.01)
            {
                await Task.Delay(50, timeout.Token);
                saved = await states.LoadAsync(accountId, timeout.Token);
            }

            await using TestClient second = await TestClient.ConnectAsync(port, sessionToken, timeout.Token);
            JoinMapResult again = await second.JoinAsync(timeout.Token);

            again.SpawnX.Should().BeApproximately(targetX, 0.01f);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task JoinMap_UnknownMap_RepliesUnknownMap()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using TestClient a = await TestClient.ConnectAsync(port, timeout.Token);

            JoinMapResult join = await a.JoinAsync(timeout.Token, "../../etc/passwd");

            join.Status.Should().Be(JoinStatus.UnknownMap);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class Inbox<T>
        where T : class
    {
        private readonly BlockingCollection<T> _items = [];

        public void Add(T item) => _items.Add(item);

        public Task<T> WaitAsync(CancellationToken ct) => Task.Run(() => _items.Take(ct), ct);
    }

    private sealed class TestClient : IAsyncDisposable
    {
        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private readonly BlockingCollection<MapSnapshot> _snapshots = [];

        private TestClient(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<MapSnapshot>(s => _snapshots.Add(Copy(s))),
                session.On<MoveCorrection>(c => this.Corrections.Add(new MoveCorrection { Sequence = c.Sequence, Reason = c.Reason, X = c.X, Y = c.Y })),
                session.On<PlayerLeft>(l => this.Left.Add(new PlayerLeft { PlayerId = l.PlayerId })),
            ];
        }

        public Inbox<MoveCorrection> Corrections { get; } = new();

        public Inbox<PlayerLeft> Left { get; } = new();

        public static async Task<TestClient> ConnectAsync(ushort port, CancellationToken ct)
        {
            WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            TestClient client = new(session);
            await session.ConnectAsync(ct: ct);
            await session.HandshakeAsync(ct);
            await client.SignInAsync(ct);
            return client;
        }

        public static async Task<TestClient> ConnectAsync(ushort port, string sessionToken, CancellationToken ct)
        {
            WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            TestClient client = new(session);
            await session.ConnectAsync(ct: ct);
            await session.HandshakeAsync(ct);
            await client.ResumeAsync(sessionToken, ct);
            return client;
        }

        public Guid AccountId { get; private set; }

        public string SessionToken { get; private set; } = string.Empty;

        private async Task SignInAsync(CancellationToken ct)
        {
            using ExternalSignIn request = new() { Provider = ExternalProvider.Google, IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, Guid.NewGuid().ToString("N")) };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
            this.SessionToken = result.SessionToken;
        }

        private async Task ResumeAsync(string sessionToken, CancellationToken ct)
        {
            using ResumeSession request = new() { SessionToken = sessionToken };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
        }

        // Saves where the player was last, so the server's join puts them there (a bad place falls back to that map's spawn).
        public async Task PutOnAsync(string mapId, CancellationToken ct)
        {
            IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
            await states.SaveAsync(this.AccountId, new PlayerStateRow(mapId, -1, -1, Nosum.Core.Facing.Down), ct);
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct, string mapId = Program.FarmMapId)
        {
            using JoinMap request = new() { MapId = mapId };
            return await _session.RequestAsync<JoinMapResult>(request, ct: ct);
        }

        public async Task SendMoveAsync(uint sequence, float x, float y, CancellationToken ct)
        {
            using MoveIntent intent = new() { Sequence = sequence, X = x, Y = y, Facing = 1, Moving = true };
            await _session.SendAsync(intent, ct);
        }

        public Task<PlayerState> WaitForPlayerAsync(uint playerId, Func<PlayerState, bool> predicate, CancellationToken ct)
            => Task.Run(() =>
            {
                foreach (MapSnapshot snapshot in _snapshots.GetConsumingEnumerable(ct))
                {
                    PlayerState? match = snapshot.Players.FirstOrDefault(p => p.PlayerId == playerId && predicate(p));
                    if (match is not null)
                    {
                        return match;
                    }
                }

                throw new InvalidOperationException("Snapshot stream ended.");
            }, ct);

        public async ValueTask DisposeAsync()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            await _session.DisconnectAsync();
            _session.Dispose();
            _snapshots.Dispose();
        }

        // Subscriber packets are pooled and returned after the callback: keep plain copies.
        private static MapSnapshot Copy(MapSnapshot source)
        {
            MapSnapshot copy = new() { ServerTimeMs = source.ServerTimeMs };
            foreach (PlayerState p in source.Players)
            {
                copy.Players.Add(new PlayerState { PlayerId = p.PlayerId, X = p.X, Y = p.Y, Facing = p.Facing, Moving = p.Moving });
            }

            return copy;
        }
    }
}
