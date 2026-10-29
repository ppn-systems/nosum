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
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>A real host over a free port with real PostgreSQL, for the chunk tests (docs/adr/ADR-022).</summary>
internal sealed class ChunkHost : IAsyncDisposable
{
    private static readonly SemaphoreSlim s_resumeGate = new(1, 1);
    private static DateTime s_lastResume = DateTime.MinValue;

    private readonly NetworkApplication _app;
    private readonly CancellationTokenSource _timeout;

    private ChunkHost(NetworkApplication app, ushort port, TimeSpan timeout)
    {
        _app = app;
        this.Port = port;
        _timeout = new CancellationTokenSource(timeout);
    }

    public ushort Port { get; }

    public CancellationToken Timeout => _timeout.Token;

    public IMapCatalog Maps => InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;

    public static async Task<ChunkHost> StartAsync(PostgresFixture database, TimeSpan timeout, bool development = false)
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        ushort port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        HostServices.Register(database.Options);
        TestAccounts.UseFakeVerifiers(database.Options);
        NetworkApplication app = Program.BuildApplication(port, null, development);
        ChunkHost host = new(app, port, timeout);
        await app.ActivateAsync(host.Timeout);
        return host;
    }

    public async Task<ChunkClient> ConnectAsync()
    {
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = this.Port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        ChunkClient client = new(session);
        await client.ConnectAsync(this.Timeout);
        return client;
    }

    // Resuming a session is limited to two a second per address and every client of a test run shares one address.
    internal static async Task PaceResumeAsync(CancellationToken ct)
    {
        await s_resumeGate.WaitAsync(ct);
        try
        {
            TimeSpan wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - s_lastResume);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            s_lastResume = DateTime.UtcNow;
        }
        finally
        {
            _ = s_resumeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DeactivateAsync(CancellationToken.None);
        await _app.DisposeAsync();
        _timeout.Dispose();
    }
}

/// <summary>One real WebSocket client that records what the server pushes to it.</summary>
internal sealed class ChunkClient : IAsyncDisposable
{
    private readonly WebSocketSession _session;
    private readonly IDisposable[] _subscriptions;
    private readonly ConcurrentQueue<LookRow> _looks = new();
    private readonly ConcurrentQueue<uint> _left = new();
    private readonly ConcurrentQueue<(string Map, int X, int Y)> _chunkLefts = new();
    private readonly ConcurrentQueue<ChunkState> _chunkStates = new();
    private readonly ConcurrentQueue<FarmTilesChanged> _farmChanges = new();
    private readonly ConcurrentQueue<PlacedChanged> _placedChanges = new();
    private readonly ConcurrentQueue<NodeChanged> _nodeChanges = new();
    private readonly ConcurrentQueue<GroundItemSpawned> _groundSpawns = new();
    private readonly ConcurrentQueue<GroundItemTaken> _groundTakens = new();
    private long _bytes;
    private int _snapshotCount;
    private long _entryCount;
    private uint[] _lastSeen = [];
    private DateTime _lastMove = DateTime.MinValue;

    public ChunkClient(WebSocketSession session)
    {
        _session = session;
        _session.OnMessageReceived += (_, lease) => Interlocked.Add(ref _bytes, lease.Length);
        _subscriptions =
        [
            session.On<MapSnapshot>(s =>
            {
                uint[] ids = new uint[s.Players.Count];
                for (int i = 0; i < ids.Length; i++)
                {
                    ids[i] = s.Players[i].PlayerId;
                }

                _ = Interlocked.Add(ref _entryCount, ids.Length);
                Volatile.Write(ref _lastSeen, ids);
                _ = Interlocked.Increment(ref _snapshotCount);
            }),
            session.On<PlayerLeft>(l => _left.Enqueue(l.PlayerId)),
            session.On<LookChanged>(l => _looks.Enqueue(new LookRow { PlayerId = l.Look.PlayerId, Nickname = l.Look.Nickname, SkinId = l.Look.SkinId })),
            session.On<ChunkLeft>(c => _chunkLefts.Enqueue((c.MapId, c.ChunkX, c.ChunkY))),
            session.On<ChunkState>(c => _chunkStates.Enqueue(Copy(c))),
            session.On<FarmTilesChanged>(f => _farmChanges.Enqueue(new FarmTilesChanged { MapId = f.MapId, Tiles = [.. f.Tiles.Select(t => new FarmTileRow { X = t.X, Y = t.Y, CropId = t.CropId })] })),
            session.On<NodeChanged>(n => _nodeChanges.Enqueue(new NodeChanged { MapId = n.MapId, Removed = [.. n.Removed], Upserted = [.. n.Upserted.Select(CopyNode)] })),
            session.On<GroundItemSpawned>(g => _groundSpawns.Enqueue(new GroundItemSpawned { MapId = g.MapId, Item = CopyGround(g.Item) })),
            session.On<GroundItemTaken>(g => _groundTakens.Enqueue(new GroundItemTaken { MapId = g.MapId, Id = g.Id, Reason = g.Reason })),
            session.On<PlacedChanged>(p => _placedChanges.Enqueue(new PlacedChanged { MapId = p.MapId, Removed = [.. p.Removed], Upserted = [.. p.Upserted.Select(o => new PlacedObjectRow { Id = o.Id, ItemId = o.ItemId, TileX = o.TileX, TileY = o.TileY, Rotation = o.Rotation })] })),
        ];
    }

    public Guid AccountId { get; private set; }

    public uint PlayerId { get; private set; }

    public int SnapshotCount => Volatile.Read(ref _snapshotCount);

    /// <summary>Gets the ids in the newest snapshot.</summary>
    public uint[] LastSeen => Volatile.Read(ref _lastSeen);

    public IReadOnlyCollection<LookRow> Looks => _looks;

    public IReadOnlyCollection<uint> Left => _left;

    public IReadOnlyCollection<(string Map, int X, int Y)> ChunkLefts => _chunkLefts;

    public IReadOnlyCollection<ChunkState> ChunkStates => _chunkStates;

    public IReadOnlyCollection<FarmTilesChanged> FarmChanges => _farmChanges;

    public IReadOnlyCollection<PlacedChanged> PlacedChanges => _placedChanges;

    public IReadOnlyCollection<NodeChanged> NodeChanges => _nodeChanges;

    public IReadOnlyCollection<GroundItemSpawned> GroundSpawns => _groundSpawns;

    public IReadOnlyCollection<GroundItemTaken> GroundTakens => _groundTakens;

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _session.ConnectAsync(ct: ct);
        await _session.HandshakeAsync(ct);

        // The account is created in the database directly: sign-ups are rate limited per address and every test of the suite shares one.
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
        created.Status.Should().Be(AccountStatus.Ok);

        await ChunkHost.PaceResumeAsync(ct);
        using ResumeSession request = new() { SessionToken = created.SessionToken! };
        AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
        result.Status.Should().Be(AccountStatus.Ok);
        this.AccountId = result.PlayerId;
    }

    // Saves where the player was last, so the server's join puts them there (a bad place falls back to that map's spawn).
    public async Task PutOnAsync(string mapId, double x, double y)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(this.AccountId, new PlayerStateRow(mapId, x, y, Nosum.Core.Facing.Down), CancellationToken.None);
    }

    public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
    {
        await WirePace.JoinAsync(ct);
        using JoinMap request = new() { MapId = Program.FarmMapId };
        JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
        result.Status.Should().Be(JoinStatus.Ok);
        this.PlayerId = result.PlayerId;
        return result;
    }

    /// <summary>Uses the item in a hotbar slot on a tile and waits for the server's answer.</summary>
    public async Task<UseItemResult> UseAsync(int slot, int tileX, int tileY, CancellationToken ct)
    {
        using UseItem request = new() { Slot = (byte)slot, TileX = tileX, TileY = tileY };
        return await _session.RequestAsync<UseItemResult>(request, ct: ct);
    }

    /// <summary>Asks for the ground items the server shows this player (the reply covers the interest set only).</summary>
    public async Task<GroundItemsSnapshot> GetGroundItemsAsync(CancellationToken ct)
    {
        using GetGroundItems request = new();
        return await _session.RequestAsync<GroundItemsSnapshot>(request, ct: ct);
    }

    /// <summary>Asks for the resource nodes the server shows this player (the reply covers the interest set only).</summary>
    public async Task<NodeSnapshot> GetNodesAsync(CancellationToken ct)
    {
        using GetNodes request = new();
        return await _session.RequestAsync<NodeSnapshot>(request, ct: ct);
    }

    /// <summary>Claims a position the way the real client does, about every 120 ms.</summary>
    public async Task SendMoveAsync(uint sequence, double x, double y, byte facing, CancellationToken ct)
    {
        TimeSpan wait = TimeSpan.FromMilliseconds(120) - (DateTime.UtcNow - _lastMove);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, ct);
        }

        _lastMove = DateTime.UtcNow;
        using MoveIntent intent = new() { Sequence = sequence, X = (float)x, Y = (float)y, Facing = facing, Moving = true };
        await _session.SendAsync(intent, ct);
    }

    /// <summary>Walks in a straight line to a point at a little under the walking speed, one claim per step.</summary>
    public async Task WalkToAsync(double fromX, double fromY, double toX, double toY, uint sequence, CancellationToken ct)
    {
        double distance = Math.Sqrt(((toX - fromX) * (toX - fromX)) + ((toY - fromY) * (toY - fromY)));
        double stepLength = Walker.Speed * 0.12 * 0.9;
        int steps = Math.Max(1, (int)Math.Ceiling(distance / stepLength));
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            await this.SendMoveAsync(sequence + (uint)i, fromX + ((toX - fromX) * t), fromY + ((toY - fromY) * t), 1, ct);
        }
    }

    /// <summary>Waits until the newest snapshot satisfies a condition.</summary>
    public async Task WaitForSeenAsync(Func<uint[], bool> condition, CancellationToken ct, string because = "")
    {
        while (!condition(this.LastSeen))
        {
            await Task.Delay(25, ct);
        }
    }

    /// <summary>Waits until a number of further snapshots arrived, so "nothing happened" is checked over real ticks.</summary>
    public async Task WaitForSnapshotsAsync(int count, CancellationToken ct)
    {
        int target = this.SnapshotCount + count;
        while (this.SnapshotCount < target)
        {
            await Task.Delay(25, ct);
        }
    }

    public async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(25, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        await _session.DisconnectAsync();
        _session.Dispose();
    }

    private static NodeRow CopyNode(NodeRow row) => new() { Id = row.Id, X = row.X, Y = row.Y, Kind = row.Kind, Variant = row.Variant, Stage = row.Stage, Hits = row.Hits };

    private static GroundItemRow CopyGround(GroundItemRow row) => new() { Id = row.Id, ItemId = row.ItemId, Quantity = row.Quantity, X = row.X, Y = row.Y };

    private static ChunkState Copy(ChunkState source)
    {
        ChunkState copy = new() { MapId = source.MapId, ChunkX = source.ChunkX, ChunkY = source.ChunkY, ServerNowMs = source.ServerNowMs };
        foreach (FarmTileRow row in source.FarmTiles)
        {
            copy.FarmTiles.Add(new FarmTileRow { X = row.X, Y = row.Y, CropId = row.CropId, PlantedAtMs = row.PlantedAtMs, GrowthDays = row.GrowthDays, WateredDay = row.WateredDay });
        }

        foreach (PlacedObjectRow row in source.Placed)
        {
            copy.Placed.Add(new PlacedObjectRow { Id = row.Id, ItemId = row.ItemId, TileX = row.TileX, TileY = row.TileY, Rotation = row.Rotation });
        }

        copy.Nodes.AddRange(source.Nodes.Select(CopyNode));
        copy.GroundItems.AddRange(source.GroundItems.Select(CopyGround));
        return copy;
    }
}

/// <summary>Finds legal standing places on a map for the chunk tests.</summary>
internal static class StandingPlaces
{
    /// <summary>Returns the feet position at the middle of a free tile inside a chunk, optionally the n-th such tile.</summary>
    public static (double X, double Y) InChunk(MapDefinition map, ChunkCoord chunk, int skip = 0)
    {
        int tile = map.Grid.TileSize;
        int found = 0;
        for (int ty = chunk.FirstTileY + 2; ty < chunk.FirstTileY + ChunkCoord.SizeTiles - 2; ty++)
        {
            for (int tx = chunk.FirstTileX + 2; tx < chunk.FirstTileX + ChunkCoord.SizeTiles - 2; tx++)
            {
                if (tx >= map.Grid.Width - 1 || ty >= map.Grid.Height - 1 || !IsFree(map, tx, ty))
                {
                    continue;
                }

                if (found++ == skip)
                {
                    return ((tx + 0.5) * tile, (ty + 0.5) * tile);
                }
            }
        }

        throw new InvalidOperationException("No free tile in chunk " + chunk + " of " + map.Id + ".");
    }

    /// <summary>Returns two standing places in chunks at least <paramref name="distance"/> chunks apart (the farthest pair the map offers), or the nearest pair of chunks <paramref name="distance"/> apart.</summary>
    public static ((ChunkCoord Chunk, double X, double Y) First, (ChunkCoord Chunk, double X, double Y) Second) Pair(MapDefinition map, int distance)
    {
        ChunkBounds bounds = ChunkBounds.ForTiles(map.Grid.Width, map.Grid.Height);
        List<(ChunkCoord Chunk, double X, double Y)> places = [];
        for (int cy = 0; cy < bounds.CountY; cy++)
        {
            for (int cx = 0; cx < bounds.CountX; cx++)
            {
                try
                {
                    (double x, double y) = InChunk(map, new ChunkCoord(cx, cy));
                    places.Add((new ChunkCoord(cx, cy), x, y));
                }
                catch (InvalidOperationException)
                {
                    // Water, cliffs or buildings fill the chunk.
                }
            }
        }

        foreach (var first in places)
        {
            foreach (var second in places)
            {
                if (first.Chunk.DistanceTo(second.Chunk) == distance)
                {
                    return (first, second);
                }
            }
        }

        throw new InvalidOperationException("No two standing places " + distance + " chunks apart on " + map.Id + ".");
    }

    /// <summary>Returns a tile column, within a chunk column, along which one can walk from one tile row to another.</summary>
    public static int ClearColumn(MapDefinition map, int chunkColumn, int fromTileY, int toTileY)
    {
        for (int tx = (chunkColumn * ChunkCoord.SizeTiles) + 3; tx < ((chunkColumn + 1) * ChunkCoord.SizeTiles) - 3 && tx < map.Grid.Width - 1; tx++)
        {
            bool clear = true;
            for (int ty = fromTileY; ty <= toTileY && clear; ty++)
            {
                clear = IsFree(map, tx, ty);
            }

            if (clear)
            {
                return tx;
            }
        }

        throw new InvalidOperationException("No clear column in chunk column " + chunkColumn + " of " + map.Id + ".");
    }

    private static bool IsFree(MapDefinition map, int tx, int ty)
    {
        int tile = map.Grid.TileSize;
        double x = (tx + 0.5) * tile;
        double y = (ty + 0.5) * tile;
        return !MoveValidator.FootBlocked(map.Grid, x, y)
            && !WarpRule.IsOnExitTile(map.Warps, map.Grid, x, y)
            && !MoveValidator.FootBlocked(map.Grid, x - tile, y)
            && !MoveValidator.FootBlocked(map.Grid, x + tile, y)
            && !WarpRule.IsOnExitTile(map.Warps, map.Grid, x - tile, y)
            && !WarpRule.IsOnExitTile(map.Warps, map.Grid, x + tile, y);
    }
}

/// <summary>Searches for the places the walking tests need.</summary>
internal static class ChunkPlaces
{
    /// <summary>
    /// Finds a row along which one can walk across two chunk borders, and a standing place for a second player two chunks to the west:
    /// player B starts just east of the border between chunk <c>c0</c> and <c>c0 + 1</c>, player A stands in chunk <c>c0 - 1</c>.
    /// </summary>
    public static CrossingPlan FindCrossing(MapDefinition map)
    {
        ChunkBounds bounds = ChunkBounds.ForTiles(map.Grid.Width, map.Grid.Height);
        for (int c0 = 1; c0 + 2 < bounds.CountX; c0++)
        {
            int from = ((c0 + 1) * ChunkCoord.SizeTiles) - 3;
            int to = ((c0 + 2) * ChunkCoord.SizeTiles) + 3;
            for (int ty = 2; ty < map.Grid.Height - 2; ty++)
            {
                if (!RowFree(map, ty, from, to))
                {
                    continue;
                }

                ChunkCoord aChunk = new(c0 - 1, ty >> ChunkCoord.Shift);
                try
                {
                    (double ax, double ay) = StandingPlaces.InChunk(map, aChunk);
                    double tile = map.Grid.TileSize;
                    return new CrossingPlan(c0, ty, ax, ay, (((c0 + 1) * ChunkCoord.SizeTiles) + 1.5) * tile, (ty + 0.5) * tile, tile);
                }
                catch (InvalidOperationException)
                {
                    // No free tile for A in that chunk: keep looking.
                }
            }
        }

        throw new InvalidOperationException("No crossing found on " + map.Id + ".");
    }

    private static bool RowFree(MapDefinition map, int ty, int fromTx, int toTx)
    {
        for (int tx = fromTx; tx <= toTx; tx++)
        {
            double x = (tx + 0.5) * map.Grid.TileSize;
            double y = (ty + 0.5) * map.Grid.TileSize;
            if (MoveValidator.FootBlocked(map.Grid, x, y) || WarpRule.IsOnExitTile(map.Warps, map.Grid, x, y))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Where two players stand for the walking test.</summary>
/// <param name="C0">Chunk column of the west side of the first border B crosses.</param>
/// <param name="Row">Tile row B walks along.</param>
/// <param name="AX">Feet x of A.</param>
/// <param name="AY">Feet y of A.</param>
/// <param name="BX">Feet x B starts at, just east of the first border.</param>
/// <param name="BY">Feet y B walks along.</param>
/// <param name="Tile">Tile size in pixels.</param>
internal readonly record struct CrossingPlan(int C0, int Row, double AX, double AY, double BX, double BY, double Tile)
{
    /// <summary>Gets the x of a tile column's middle.</summary>
    public double ColumnX(int tileX) => (tileX + 0.5) * this.Tile;
}

/// <summary>
/// A chunk index and publisher that know nobody, for tests that build a service by hand over a fake presence and look at its state, not at what it publishes.
/// </summary>
internal sealed class NoChunks : IChunkMap, IChunkPublisher
{
    public static NoChunks Instance { get; } = new();

    public void Subscribe(PlayerPresence player, ChunkCoord center, ChunkBounds bounds)
    {
    }

    public bool Relocate(PlayerPresence player, ChunkCoord destination, Span<ChunkCoord> entered, out int enteredCount, Span<ChunkCoord> left, out int leftCount)
    {
        enteredCount = 0;
        leftCount = 0;
        return false;
    }

    public bool IsInterested(PlayerPresence player, ChunkCoord chunk) => false;

    public void CopySubscribers(MapInstance instance, ChunkCoord chunk, List<PlayerPresence> into)
    {
    }

    public void CopyVisiblePlayers(PlayerPresence viewer, List<PlayerPresence> into) => into.Add(viewer);

    public ValueTask PublishAsync(MapInstance instance, ChunkCoord chunk, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask PublishInstanceAsync(MapInstance instance, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>
/// A publisher that sends every chunk or instance publish to the whole instance through a presence, for tests that build a service by hand over a
/// fake presence and assert on what it published, without chunk interest.
/// </summary>
internal sealed class InstanceWidePublisher(IMapPresence presence) : IChunkPublisher
{
    public ValueTask PublishAsync(MapInstance instance, ChunkCoord chunk, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        => presence.MulticastAsync(instance.Key, packet, except, cancellationToken);

    public ValueTask PublishInstanceAsync(MapInstance instance, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        => presence.MulticastAsync(instance.Key, packet, except, cancellationToken);
}
