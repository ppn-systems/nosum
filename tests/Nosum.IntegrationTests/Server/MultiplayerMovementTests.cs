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
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>Two real Nalix WebSocket clients against the real host on a free port.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class MultiplayerMovementTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task MoveIntent_FromPlayerA_ReachesPlayerBInSnapshots()
    {
        ushort port = GetFreePort();
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using TestClient a = await TestClient.ConnectAsync(port, timeout.Token);
            await using TestClient b = await TestClient.ConnectAsync(port, timeout.Token);

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
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using TestClient b = await TestClient.ConnectAsync(port, timeout.Token);
            await b.JoinAsync(timeout.Token);
            uint idOfA;
            await using (TestClient a = await TestClient.ConnectAsync(port, timeout.Token))
            {
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
    public async Task JoinMap_UnknownMap_RepliesUnknownMap()
    {
        ushort port = GetFreePort();
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
            return client;
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
