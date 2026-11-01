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
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Community;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Community;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Infrastructure.Community;
using Nosum.Infrastructure.Persistence;
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
    public async Task Move_ontoTheFarmhouseDoor_MovesThePlayerIntoTheirOwnFarmhouse_WhereTheyAreTheOnlyOne()
    {
        await using Host host = await Host.StartAsync(_database);
        await using WorldClient a = await host.ConnectAsync();
        await using WorldClient b = await host.ConnectAsync();
        JoinMapResult joinA = await a.JoinAsync(host.Timeout);
        JoinMapResult joinB = await b.JoinAsync(host.Timeout);
        joinA.MapId.Should().Be(GameMaps.Farm, "a new player starts on their own farm");
        joinB.MapId.Should().Be(GameMaps.Farm);

        await a.SendMoveAsync(1, joinA.SpawnX, DoorFeetY, Up, host.Timeout);

        MapChanged entered = await a.MapChanges.WaitAsync(host.Timeout);
        entered.MapId.Should().Be(GameMaps.FarmHouse);
        entered.PlayerId.Should().Be(joinA.PlayerId, "the player keeps their id across maps");
        entered.Facing.Should().Be(Up);

        // The first snapshot after the map change is A's own farmhouse: A sees itself, never B. B never saw A on the farm either.
        Snapshot seenByA = await a.WaitForSnapshotAsync(s => s.MapChangesSeen >= 1, host.Timeout);
        seenByA.Players.Should().Contain(joinA.PlayerId).And.NotContain(joinB.PlayerId);
        (await b.NextSnapshotAsync(host.Timeout)).Players.Should().ContainSingle().Which.Should().Be(joinB.PlayerId);

        // Walking down on the arrival tile (the way out) does nothing: an arrival is not sent straight back.
        await a.SendMoveAsync(2, entered.X, entered.Y, Down, host.Timeout);
        await a.MapChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));

        // The way back: step off the exit tile first, then walk out of the door.
        await a.SendMoveAsync(3, entered.X, entered.Y - 16, Up, host.Timeout);
        await a.SendMoveAsync(4, entered.X, entered.Y, Down, host.Timeout);
        MapChanged back = await a.MapChanges.WaitAsync(host.Timeout);
        back.MapId.Should().Be(GameMaps.Farm);
        back.PlayerId.Should().Be(joinA.PlayerId);
        (await a.WaitForSnapshotAsync(s => s.MapChangesSeen >= 2, host.Timeout)).Players.Should().ContainSingle().Which.Should().Be(joinA.PlayerId);
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

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(40)]
    [InlineData(120)]
    public async Task Disconnect_RightAfterTheMoveOntoAnExit_LeavesNobodyOnAnyMap_AndTheSessionRejoinsOnTheOldOrTheNewMap(int closeAfterMs)
    {
        await using Host host = await Host.StartAsync(_database);
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        string sessionToken;
        Guid accountId;
        await using (WorldClient first = await host.ConnectAsync())
        {
            sessionToken = first.SessionToken;
            accountId = first.AccountId;
            JoinMapResult join = await first.JoinAsync(host.Timeout);
            await first.SendMoveAsync(1, join.SpawnX, DoorFeetY, Up, host.Timeout);

            // The connection closes while the server may be in the middle of moving the player into the farmhouse.
            await Task.Delay(closeAfterMs, host.Timeout);
        }

        // Nobody is left behind: the presence record of the dead connection is gone whichever side of the map change it fell on.
        while (presence.PlayerCount != 0)
        {
            await Task.Delay(25, host.Timeout);
        }

        // The record goes first (MapPresence.LeaveAsync); the leave handler then saves the place. Wait for that save, not for the record.
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        PlayerStateRow? saved = await WaitForSavedPlaceAsync(states, accountId, host.Timeout);
        saved.Should().NotBeNull();
        saved!.Value.MapName.Should().BeOneOf(GameMaps.Farm, GameMaps.FarmHouse);

        await using WorldClient second = await host.ConnectAsync(sessionToken);
        JoinMapResult again = await second.JoinAsync(host.Timeout);
        again.Status.Should().Be(JoinStatus.Ok);
        again.MapId.Should().BeOneOf(GameMaps.Farm, GameMaps.FarmHouse);
        (await second.WaitForSnapshotAsync(s => s.Contains(again.PlayerId), host.Timeout)).Players.Should().ContainSingle("the player is the only one on their map: no ghost of the first connection");
        presence.PlayerCount.Should().Be(1);
    }

    [Fact]
    public async Task Walk_TownToTheMountainThroughTheNorthRoad_ChangesMap()
    {
        await using Host host = await Host.StartAsync(_database);
        await using WorldClient a = await host.ConnectAsync();
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(a.AccountId, new PlayerStateRow(GameMaps.Town, (81 * 16) + 8, (3 * 16) + 8, Nosum.Core.Facing.Up), host.Timeout);
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        join.MapId.Should().Be(GameMaps.Town);

        uint sequence = 0;
        float y = join.SpawnY;
        while (a.MapChanges.IsEmpty && y > 1)
        {
            y = Math.Max(y - 8, 1);
            await a.SendMoveAsync(++sequence, join.SpawnX, y, Up, host.Timeout);
        }

        MapChanged onTheMountain = await a.MapChanges.WaitAsync(host.Timeout);
        onTheMountain.MapId.Should().Be(GameMaps.Mountain);
    }

    [Fact]
    public async Task Walk_TownToTheBeachAndBack_ChangesMapEachWay_AndTheBridgeIsNotYetBuilt()
    {
        await using Host host = await Host.StartAsync(_database);
        await using WorldClient a = await host.ConnectAsync();
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;

        // On the Town's southern road, two tiles above the edge that the file's exit (53..55, 110) uses.
        await states.SaveAsync(a.AccountId, new PlayerStateRow(GameMaps.Town, (54 * 16) + 8, (107 * 16) + 8, Nosum.Core.Facing.Down), host.Timeout);
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        join.MapId.Should().Be(GameMaps.Town);
        join.Unlocks.Should().Be(0, "no community goal is complete");

        uint sequence = 0;
        float walkY = join.SpawnY;
        while (a.MapChanges.IsEmpty && walkY < (110 * 16) - 1)
        {
            walkY = Math.Min(walkY + 8, (110 * 16) - 1);
            await a.SendMoveAsync(++sequence, join.SpawnX, walkY, Down, host.Timeout);
        }

        MapChanged onTheBeach = await a.MapChanges.WaitAsync(host.Timeout);
        onTheBeach.MapId.Should().Be(GameMaps.Beach);
        onTheBeach.PlayerId.Should().Be(join.PlayerId);
        onTheBeach.Unlocks.Should().Be(0);
        ((int)(onTheBeach.Y / 16)).Should().Be(0, "the road arrives on the top edge of the Beach");

        // The way back: step off the arrival tile, then walk up out of the top edge.
        await a.MapChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(300));
        float y = onTheBeach.Y;
        for (int i = 0; i < 2; i++)
        {
            y += 8;
            await a.SendMoveAsync(++sequence, onTheBeach.X, y, Down, host.Timeout);
        }

        while (a.MapChanges.IsEmpty && y > 0)
        {
            y -= 8;
            await a.SendMoveAsync(++sequence, onTheBeach.X, y, Up, host.Timeout);
        }

        MapChanged back = await a.MapChanges.WaitAsync(host.Timeout);
        back.MapId.Should().Be(GameMaps.Town);
        back.PlayerId.Should().Be(join.PlayerId);
    }

    [Fact]
    public async Task BeachBridge_IsBlockedUntilTheCommunityUnlock_ThenWalkable_AndTheClientIsToldAtTheNextJoin()
    {
        await using Host host = await Host.StartAsync(_database);
        await using WorldClient a = await host.ConnectAsync();
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        CommunityUnlocks unlocks = (CommunityUnlocks)InstanceManager.Instance.GetExistingInstance<ICommunityUnlocks>()!;

        // On the west bank, facing the bridge (tiles 58..61 of row 13).
        float feetY = (OutdoorWorld.BridgeY * 16) + 15;
        await states.SaveAsync(a.AccountId, new PlayerStateRow(GameMaps.Beach, (OutdoorWorld.BridgeWestBank.X * 16) + 8, feetY, Nosum.Core.Facing.Right), host.Timeout);
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        join.MapId.Should().Be(GameMaps.Beach);
        join.Unlocks.Should().Be(0);

        uint sequence = 0;
        float x = join.SpawnX;
        while (a.Corrections.IsEmpty && x < (OutdoorWorld.BridgeEastBank.X + 2) * 16)
        {
            x += 8;
            await a.SendMoveAsync(++sequence, x, feetY, Right, host.Timeout);
        }

        MoveCorrection wall = await a.Corrections.WaitAsync(host.Timeout);
        wall.Reason.Should().Be(MoveRefusal.Blocked, "the planks are missing");
        wall.X.Should().BeLessThan(OutdoorWorld.BridgeFromX * 16, "the walker stays on the west bank");

        // The goal "Spring bridge" completes: the flag is in the table and the server's set.
        await using (NosumDbContext db = new(_database.Options))
        {
            db.CommunityUnlocks.Add(new CommunityUnlockRecord { Key = CommunityUnlockKeys.BeachBridge, GoalId = 1, UnlockedAt = DateTimeOffset.UtcNow });
            _ = await db.SaveChangesAsync(host.Timeout);
        }

        try
        {
            await unlocks.LoadAsync(host.Timeout);
            await Task.Delay(400, host.Timeout);
            a.Corrections.Clear();

            // The same grid now lets a walker across, tile by tile, to the east bank.
            x = wall.X;
            uint crossing = sequence + 100;
            while (x < (OutdoorWorld.BridgeEastBank.X + 1) * 16)
            {
                x += 8;
                await a.SendMoveAsync(++crossing, x, feetY, Right, host.Timeout);
            }

            await a.Corrections.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));

            // A client that joins now is told, so its own grid opens the same tiles.
            await using WorldClient b = await host.ConnectAsync();
            await states.SaveAsync(b.AccountId, new PlayerStateRow(GameMaps.Beach, (OutdoorWorld.BridgeWestBank.X * 16) + 8, feetY, Nosum.Core.Facing.Right), host.Timeout);
            (await b.JoinAsync(host.Timeout)).Unlocks.Should().Be((byte)CommunityUnlockSet.BeachBridge);
        }
        finally
        {
            await using NosumDbContext db = new(_database.Options);
            _ = await db.CommunityUnlocks.Where(u => u.Key == CommunityUnlockKeys.BeachBridge).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    // Feet y on the farmhouse door tile (64, 15), straight above the spawn tile (64, 16).
    private const float DoorFeetY = (15 * 16) + 15;

    // Polls the saved place until the leave handler's write has landed: the state cache is updated only after that write commits.
    private static async Task<PlayerStateRow?> WaitForSavedPlaceAsync(IPlayerStateService states, Guid accountId, CancellationToken cancellationToken)
    {
        while (true)
        {
            PlayerStateRow? saved = await states.LoadAsync(accountId, cancellationToken);
            if (saved is not null)
            {
                return saved;
            }

            await Task.Delay(25, cancellationToken);
        }
    }

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
            TestAccounts.UseFakeVerifiers(database.Options);
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

        public void Clear()
        {
            while (_items.TryTake(out _))
            {
            }
        }

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
                    this.MapChanges.Add(new MapChanged { PlayerId = m.PlayerId, MapId = m.MapId, X = m.X, Y = m.Y, Facing = m.Facing, Unlocks = m.Unlocks });
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
                using ExternalSignIn request = new() { Provider = ExternalProvider.Google, IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, Guid.NewGuid().ToString("N")) };
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
            await WirePace.JoinAsync(ct);
            using JoinMap request = new() { MapId = Program.FarmMapId };
            // The first join of a fresh player loads its farm from the database; the SDK's 5 s default is too tight with the whole suite running.
            return await _session.RequestAsync<JoinMapResult>(request, RequestOptions.Default.WithTimeout(15_000), ct: ct);
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
