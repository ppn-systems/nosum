// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The server moves players between maps when they walk onto an exit: two real Nalix WebSocket clients against
/// the real host and a real PostgreSQL database.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class MapChangeTests : IClassFixture<PostgresFixture>
{
    private const byte Down = 0;
    private const byte Right = 1;
    private const byte Up = 2;

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    private readonly PostgresFixture _database;

    public MapChangeTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Move_ontoTheFarmhouseDoor_MovesPlayerAToTheFarmhouseAndPlayerBStopsSeeingThem()
    {
        await using Host host = await Host.StartAsync(_database);
        await using WorldClient a = await host.ConnectAsync();
        await using WorldClient b = await host.ConnectAsync();
        JoinMapResult joinA = await a.JoinAsync(host.Timeout);
        JoinMapResult joinB = await b.JoinAsync(host.Timeout);
        joinA.MapId.Should().Be(GameMaps.Farm, "a new player starts on the farm");
        await b.WaitForSnapshotAsync(s => s.Contains(joinA.PlayerId), host.Timeout);

        await a.SendMoveAsync(1, joinA.SpawnX, DoorFeetY, Up, host.Timeout);

        MapChanged entered = await a.MapChanges.WaitAsync(host.Timeout);
        entered.MapId.Should().Be(GameMaps.FarmHouse);
        entered.PlayerId.Should().Be(joinA.PlayerId, "the player keeps their id across maps");
        entered.Facing.Should().Be(Up);
        (await b.Left.WaitAsync(host.Timeout)).PlayerId.Should().Be(joinA.PlayerId);
        await b.WaitForSnapshotAsync(s => !s.Contains(joinA.PlayerId), host.Timeout);
        (await b.NextSnapshotAsync(host.Timeout)).Players.Should().NotContain(joinA.PlayerId);
        (await b.NextSnapshotAsync(host.Timeout)).Players.Should().NotContain(joinA.PlayerId);

        // The first snapshot after the map change is the farmhouse's: A sees itself, not B.
        Snapshot seenByA = await a.WaitForSnapshotAsync(s => s.MapChangesSeen >= 1, host.Timeout);
        seenByA.Players.Should().Contain(joinA.PlayerId).And.NotContain(joinB.PlayerId);

        // Walking down on the arrival tile (the way out) does nothing: an arrival is not sent straight back.
        await a.SendMoveAsync(2, entered.X, entered.Y, Down, host.Timeout);
        await a.MapChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));

        // The way back: step off the exit tile first, then walk out of the door.
        await a.SendMoveAsync(3, entered.X, entered.Y - 16, Up, host.Timeout);
        await a.SendMoveAsync(4, entered.X, entered.Y, Down, host.Timeout);
        MapChanged back = await a.MapChanges.WaitAsync(host.Timeout);
        back.MapId.Should().Be(GameMaps.Farm);
        back.PlayerId.Should().Be(joinA.PlayerId);
        await b.WaitForSnapshotAsync(s => s.Contains(joinA.PlayerId), host.Timeout);
    }

    [Fact]
    public async Task Move_ThatIsNotOnAnExit_DoesNotChangeMap_AndWalkingOffTheEastEdgeEntersTheBusStop()
    {
        await using Host host = await Host.StartAsync(_database);
        await using WorldClient a = await host.ConnectAsync();
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(a.AccountId, new PlayerStateRow("removed-map", 40, 40, Nosum.Core.Facing.Down), host.Timeout);
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        join.MapId.Should().Be(GameMaps.Farm);

        // Into the wall beside the door: refused as blocked. Across the map: refused as too fast.
        await a.SendMoveAsync(1, join.SpawnX - 20, DoorFeetY, Up, host.Timeout);
        MoveCorrection wall = await a.Corrections.WaitAsync(host.Timeout);
        await a.SendMoveAsync(2, join.SpawnX + 100, join.SpawnY, Right, host.Timeout);
        MoveCorrection far = await a.Corrections.WaitAsync(host.Timeout);

        wall.Reason.Should().Be(MoveRefusal.Blocked);
        far.Reason.Should().Be(MoveRefusal.TooFast);
        await a.MapChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));

        // Walk east along the row of the farmhouse path at walking speed (8 px per 100 ms) to the exit on the edge.
        float x = join.SpawnX;
        uint sequence = 2;
        while (a.MapChanges.IsEmpty && x < 79.9f * 16)
        {
            x += 8;
            await a.SendMoveAsync(++sequence, x, join.SpawnY, Right, host.Timeout);
            await Task.Delay(100, host.Timeout);
        }

        MapChanged entered = await a.MapChanges.WaitAsync(host.Timeout);
        entered.MapId.Should().Be(GameMaps.BusStop);
        entered.PlayerId.Should().Be(join.PlayerId);
        (await a.WaitForSnapshotAsync(s => s.MapChangesSeen >= 1, host.Timeout)).Players.Should().ContainSingle().Which.Should().Be(join.PlayerId);
    }

    [Fact]
    public async Task Join_AfterChangingMap_ReturnsToTheSavedMapAndPlace()
    {
        await using Host host = await Host.StartAsync(_database);
        string sessionToken;
        Guid accountId;
        MapChanged entered;
        await using (WorldClient first = await host.ConnectAsync())
        {
            sessionToken = first.SessionToken;
            accountId = first.AccountId;
            JoinMapResult join = await first.JoinAsync(host.Timeout);
            await first.SendMoveAsync(1, join.SpawnX, DoorFeetY, Up, host.Timeout);
            entered = await first.MapChanges.WaitAsync(host.Timeout);
        }

        // The place is saved when the server notices the disconnect (and already when the map changed).
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        PlayerStateRow? saved = null;
        while (saved is null || saved.Value.MapName != GameMaps.FarmHouse)
        {
            await Task.Delay(50, host.Timeout);
            saved = await states.LoadAsync(accountId, host.Timeout);
        }

        await using WorldClient second = await host.ConnectAsync(sessionToken);
        JoinMapResult again = await second.JoinAsync(host.Timeout);

        again.Status.Should().Be(JoinStatus.Ok);
        again.MapId.Should().Be(GameMaps.FarmHouse);
        again.SpawnX.Should().BeApproximately(entered.X, 0.01f);
        again.SpawnY.Should().BeApproximately(entered.Y, 0.01f);
        (await second.WaitForSnapshotAsync(s => s.Contains(again.PlayerId), host.Timeout)).Players.Should().ContainSingle();
    }

    // Feet y on the farmhouse door tile (64, 15), straight above the spawn tile (64, 16).
    private const float DoorFeetY = (15 * 16) + 15;

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>A running host with a timeout that covers the whole test.</summary>
    private sealed class Host : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);
        private readonly ushort _port;

        private Host(NetworkApplication app, ushort port)
        {
            _app = app;
            _port = port;
        }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<Host> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            NetworkApplication app = Program.BuildApplication(port);
            Host host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<WorldClient> ConnectAsync(string? sessionToken = null)
        {
            WorldClient client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = _port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(sessionToken, this.Timeout);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }

    private sealed class Inbox<T>
        where T : class
    {
        private readonly BlockingCollection<T> _items = [];

        public bool IsEmpty => _items.Count == 0;

        public void Add(T item) => _items.Add(item);

        public Task<T> WaitAsync(CancellationToken ct) => Task.Run(() => _items.Take(ct), ct);

        public async Task ExpectNothingAsync(TimeSpan time)
        {
            using CancellationTokenSource limit = new(time);
            try
            {
                T unexpected = await Task.Run(() => _items.Take(limit.Token));
                throw new InvalidOperationException("Unexpected " + unexpected.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                // Nothing arrived in time.
            }
        }
    }

    /// <summary>A signed-in client that remembers the pushes it needs.</summary>
    private sealed class WorldClient : IAsyncDisposable
    {
        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private readonly BlockingCollection<Snapshot> _snapshots = [];
        private DateTime _lastMove = DateTime.MinValue;
        private int _mapChanges;

        public WorldClient(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<MapSnapshot>(s => _snapshots.Add(new Snapshot([.. s.Players.Select(p => p.PlayerId)], Volatile.Read(ref _mapChanges)))),
                session.On<MoveCorrection>(c => this.Corrections.Add(new MoveCorrection { Sequence = c.Sequence, Reason = c.Reason, X = c.X, Y = c.Y })),
                session.On<PlayerLeft>(l => this.Left.Add(new PlayerLeft { PlayerId = l.PlayerId })),
                session.On<MapChanged>(m =>
                {
                    _ = Interlocked.Increment(ref _mapChanges);
                    this.MapChanges.Add(new MapChanged { PlayerId = m.PlayerId, MapId = m.MapId, X = m.X, Y = m.Y, Facing = m.Facing });
                }),
            ];
        }

        public Inbox<MoveCorrection> Corrections { get; } = new();

        public Inbox<PlayerLeft> Left { get; } = new();

        public Inbox<MapChanged> MapChanges { get; } = new();

        public Guid AccountId { get; private set; }

        public string SessionToken { get; private set; } = string.Empty;

        public async Task ConnectAsync(string? sessionToken, CancellationToken ct)
        {
            await _session.ConnectAsync(ct: ct);
            await _session.HandshakeAsync(ct);
            if (sessionToken is null)
            {
                using Register request = new() { Email = "w" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com", Password = "password-1" };
                AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
                result.Status.Should().Be(AccountStatus.Ok);
                this.AccountId = result.PlayerId;
                this.SessionToken = result.SessionToken;
            }
            else
            {
                using ResumeSession request = new() { SessionToken = sessionToken };
                AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
                result.Status.Should().Be(AccountStatus.Ok);
                this.AccountId = result.PlayerId;
                this.SessionToken = sessionToken;
            }
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            return await _session.RequestAsync<JoinMapResult>(request, ct: ct);
        }

        public async Task SendMoveAsync(uint sequence, float x, float y, byte facing, CancellationToken ct)
        {
            // The real client sends about every 100 ms; the host drops intents that arrive right behind one another.
            TimeSpan wait = TimeSpan.FromMilliseconds(120) - (DateTime.UtcNow - _lastMove);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            _lastMove = DateTime.UtcNow;
            using MoveIntent intent = new() { Sequence = sequence, X = x, Y = y, Facing = facing, Moving = true };
            await _session.SendAsync(intent, ct);
        }

        /// <summary>Returns the next snapshot that satisfies the predicate.</summary>
        public Task<Snapshot> WaitForSnapshotAsync(Func<Snapshot, bool> predicate, CancellationToken ct)
            => Task.Run(() =>
            {
                foreach (Snapshot snapshot in _snapshots.GetConsumingEnumerable(ct))
                {
                    if (predicate(snapshot))
                    {
                        return snapshot;
                    }
                }

                throw new InvalidOperationException("Snapshot stream ended.");
            }, ct);

        public Task<Snapshot> NextSnapshotAsync(CancellationToken ct) => Task.Run(() => _snapshots.Take(ct), ct);

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
    }

    /// <summary>The players of one snapshot and how many map changes the client had been told of when it arrived.</summary>
    private sealed record Snapshot(List<uint> Players, int MapChangesSeen)
    {
        public bool Contains(uint playerId) => this.Players.Contains(playerId);
    }
}
