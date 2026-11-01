// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Fishing.C2S;
using Nosum.Contracts.Fishing.S2C;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Nodes;
using Nosum.Host;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Persistence;

namespace Nosum.IntegrationTests.Server;

public sealed partial class NodeWireTests
{
    internal static int SlotOf(int itemId) => Array.FindIndex(StarterKit.Items, i => i.ItemId == itemId);

    internal static WorldNode Row(NodeKind kind, int x, int y, byte variant = 0, byte stage = TreeStage.Grown)
        => new()
        {
            TileX = x,
            TileY = y,
            Kind = (short)kind,
            Variant = variant,
            Stage = kind == NodeKind.Tree ? (short)stage : (short)0,
            Hits = NodeCatalog.HitsFor(kind, stage),
            SpawnedAt = s_start,
        };

    // Replaces the nodes of one instance with exactly these and marks it seeded, so the map's random first fill never happens.
    internal static async Task SeedAsync(PostgresFixture database, string mapId, Guid owner, params WorldNode[] nodes)
    {
        await using NosumDbContext db = new(database.Options);
        _ = await db.WorldNodes.Where(n => n.MapId == mapId && n.OwnerId == owner).ExecuteDeleteAsync();
        _ = await db.NodeInstances.Where(n => n.MapId == mapId && n.OwnerId == owner).ExecuteDeleteAsync();
        foreach (WorldNode node in nodes)
        {
            node.MapId = mapId;
            node.OwnerId = owner;
            db.WorldNodes.Add(node);
        }

        db.NodeInstances.Add(new NodeInstanceState { MapId = mapId, OwnerId = owner, SeededAt = s_start, LastDay = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today });
        _ = await db.SaveChangesAsync();
    }

    internal Task SeedAsync(string mapId, Guid owner, params WorldNode[] nodes)
        => SeedAsync(_database, mapId, owner, nodes);

    // Seeds the farm of a new player with the given nodes (kind, species) at tiles that will be in reach of the spawn, then joins.
    internal static async Task<JoinMapResult> SeededJoinAsync(PostgresFixture database, TestHost host, ToolClient player, (NodeKind Kind, byte Variant)[] nodes)
    {
        (int X, int Y)[] free = host.FreeTilesNear(GameMaps.Farm);
        WorldNode[] rows = [.. nodes.Select((n, i) => Row(n.Kind, free[i].X, free[i].Y, n.Variant))];
        await SeedAsync(database, GameMaps.Farm, player.AccountId, rows);
        return await player.JoinAsync(host.Timeout);
    }

    internal Task<JoinMapResult> SeededJoinAsync(TestHost host, ToolClient player, (NodeKind Kind, byte Variant)[] nodes)
        => SeededJoinAsync(_database, host, player, nodes);

    // Three open tiles in a row, with the middle one for a node: the tiles left and right are where two players stand.
    private static (int Left, int Middle, int Right, int Row) OpenRow(MapDefinition map)
    {
        int startX = (int)(map.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        for (int radius = 0; radius < 30; radius++)
        {
            for (int y = startY - radius; y <= startY + radius; y++)
            {
                for (int x = startX - radius; x <= startX + radius; x++)
                {
                    if (!map.Grid.IsBlocked(x - 1, y) && !map.Grid.IsBlocked(x, y) && !map.Grid.IsBlocked(x + 1, y) && !map.ReservedTiles.Contains((x, y)))
                    {
                        return (x - 1, x, x + 1, y);
                    }
                }
            }
        }

        throw new InvalidOperationException("No open row on " + map.Id);
    }

    // A water tile with a walkable tile beside it, and that walkable tile.
    internal static (int WaterX, int WaterY, int StandX, int StandY) ShoreOf(MapDefinition map)
    {
        for (int y = 1; y < map.Grid.Height - 1; y++)
        {
            for (int x = 1; x < map.Grid.Width - 1; x++)
            {
                if (!map.Water!.IsWater(x, y))
                {
                    continue;
                }

                foreach ((int dx, int dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    if (!map.Grid.IsBlocked(x + dx, y + dy) && !map.Water.IsWater(x + dx, y + dy))
                    {
                        return (x, y, x + dx, y + dy);
                    }
                }
            }
        }

        throw new InvalidOperationException("No shore on " + map.Id);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    internal sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private TestHost(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider? clock = null, bool development = true)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);

            // Development by default, so the tester tools (grant, clock, QA hook) are mapped; a Production host is started with development: false.
            NetworkApplication app = Program.BuildApplication(port, clock, development: development);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<ToolClient> ConnectAsync(string? sessionToken = null)
        {
            ToolClient client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = this.Port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(sessionToken, this.Timeout);
            return client;
        }

        // Walkable, unreserved tiles around the map's spawn, nearest first, that are within reach of the spawn feet.
        public (int X, int Y)[] FreeTilesNear(string mapId)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(mapId, out MapDefinition? map);
            int tx = (int)(map!.SpawnX / 16);
            int ty = (int)(map.SpawnY / 16);
            return [.. Enumerable.Range(-2, 5)
                .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
                .Where(t => !map.Grid.IsBlocked(t.X, t.Y) && !map.ReservedTiles.Contains((t.X, t.Y)) && !(t.X == tx && t.Y == ty) && FarmRules.InReach(map.SpawnX, map.SpawnY, t.X, t.Y, 16))
                .OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty))];
        }

        public (int X, int Y) TileInReach(JoinMapResult join, string mapId, int index)
        {
            _ = join;
            (int X, int Y)[] free = this.FreeTilesNear(mapId);
            return free[index];
        }

        public (int X, int Y) TillableTileInReach(JoinMapResult join)
        {
            _ = join;
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
            return this.FreeTilesNear(GameMaps.Farm).First(t => farm!.Tillable!.IsTillable(t.X, t.Y));
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }

    // Pushes arrive on socket threads; tests read them in order and skip what does not match.
    internal sealed class Inbox<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public void Add(T item) => _ = _items.Writer.TryWrite(item);

        public async Task<T> WaitAsync(Func<T, bool> predicate, CancellationToken ct)
        {
            while (true)
            {
                T item = await _items.Reader.ReadAsync(ct);
                if (predicate(item))
                {
                    return item;
                }
            }
        }

        // Nothing matching may arrive in the given time.
        public async Task ExpectNothingAsync(TimeSpan time)
        {
            using CancellationTokenSource limit = new(time);
            try
            {
                T unexpected = await this.WaitAsync(_ => true, limit.Token);
                throw new InvalidOperationException("Unexpected " + unexpected!.GetType().Name);
            }
            catch (OperationCanceledException)
            {
                // Nothing arrived in time.
            }
        }
    }

    internal sealed class ToolClient : IAsyncDisposable
    {
        // The farm-use handler allows a burst and then eight a second per address; the server's swing cooldown is 300 ms.
        private static readonly TimeSpan s_swing = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private DateTime _lastMove = DateTime.MinValue;

        public ToolClient(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<NodeChanged>(p => this.NodeChanges.Add(new NodeChanged { MapId = p.MapId, Upserted = [.. p.Upserted.Select(Copy)], Removed = [.. p.Removed] })),
                session.On<ToolState>(p => this.Tools.Add(new ToolState { WaterLeft = p.WaterLeft, WaterCapacity = p.WaterCapacity })),
                session.On<FarmTilesCleared>(p => this.Cleared.Add(new FarmTilesCleared { MapId = p.MapId, Tiles = [.. p.Tiles.Select(t => new TileRow { X = t.X, Y = t.Y })] })),
                session.On<FarmTilesChanged>(p =>
                {
                    foreach (FarmTileRow t in p.Tiles)
                    {
                        this.TileChanges.Add(new FarmTileRow { X = t.X, Y = t.Y, CropId = t.CropId });
                    }
                }),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        this.Slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
                session.On<MoveCorrection>(c => this.Corrections.Add(new MoveCorrection { Sequence = c.Sequence, Reason = c.Reason, X = c.X, Y = c.Y })),
                session.On<LevelUp>(p => this.LevelUps.Add((p.Skill, p.Level))),
            ];
        }

        public Inbox<NodeChanged> NodeChanges { get; } = new();

        public Inbox<ToolState> Tools { get; } = new();

        public Inbox<FarmTilesCleared> Cleared { get; } = new();

        public Inbox<FarmTileRow> TileChanges { get; } = new();

        public Inbox<InventorySlotRow> Slots { get; } = new();

        public Inbox<MoveCorrection> Corrections { get; } = new();

        public Inbox<(byte Skill, byte Level)> LevelUps { get; } = new();

        public Guid AccountId { get; private set; }

        public string SessionToken { get; private set; } = string.Empty;

        public async Task ConnectAsync(string? sessionToken, CancellationToken ct)
        {
            await _session.ConnectAsync(ct: ct);
            await _session.HandshakeAsync(ct);
            if (sessionToken is null)
            {
                // The account is created in the database directly: sign-ups are rate limited per address.
                IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
                AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
                created.Status.Should().Be(AccountStatus.Ok);
                sessionToken = created.SessionToken!;
            }

            await WirePace.WaitAsync(ct);
            using ResumeSession request = new() { SessionToken = sessionToken };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
            this.SessionToken = sessionToken;
        }

        // Saves where the player was last, so the server's join puts them there.
        public async Task PutOnAsync(string mapId, float x, float y)
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
            return result;
        }

        public async Task<NodeSnapshot> GetNodesAsync(CancellationToken ct)
        {
            await Task.Delay(250, ct);
            using GetNodes request = new();
            return await _session.RequestAsync<NodeSnapshot>(request, ct: ct);
        }

        // Waits out the swing cooldown first, like a player who keeps swinging.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            return await this.UseNowAsync(slot, x, y, ct);
        }

        public async Task<UseItemResult> UseNowAsync(int slot, int x, int y, CancellationToken ct)
        {
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        public async Task<InventoryStatus> SelectSlotAsync(int slot, CancellationToken ct)
        {
            using SelectSlot request = new() { Slot = (byte)slot };
            return (await _session.RequestAsync<InventoryResult>(request, ct: ct)).Status;
        }

        public async Task<CastResult> CastRodAsync(int tileX, int tileY, CancellationToken ct)
        {
            using CastRod request = new() { TileX = tileX, TileY = tileY };
            return await _session.RequestAsync<CastResult>(request, ct: ct);
        }

        public async Task<ReelResult> ReelInAsync(uint castId, CancellationToken ct)
        {
            using ReelIn request = new() { CastId = castId };
            return await _session.RequestAsync<ReelResult>(request, ct: ct);
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task<bool> TeleportAsync(string mapId, int tileX, int tileY, CancellationToken ct)
        {
            using DevTeleport request = new() { MapId = mapId, TileX = tileX, TileY = tileY };
            return (await _session.RequestAsync<DevTeleportResult>(request, ct: ct)).Ok;
        }

        public async Task<bool> SeasonAsync(int seasons, CancellationToken ct)
        {
            using DevSeason request = new() { Seasons = seasons };
            return (await _session.RequestAsync<DevSeasonResult>(request, ct: ct)).Ok;
        }

        public async Task<bool> LevelUpAsync(int skill, int level, CancellationToken ct)
        {
            using DevLevelUp request = new() { Skill = skill, Level = level };
            return (await _session.RequestAsync<DevLevelUpResult>(request, ct: ct)).Ok;
        }

        public async Task<bool> BiteAsync(uint castId, CancellationToken ct)
        {
            using DevFishingBite request = new() { CastId = castId };
            return (await _session.RequestAsync<DevFishingBiteResult>(request, ct: ct)).Ok;
        }

        /// <summary>Probes a Development opcode with a short timeout: <see langword="true"/> when the server answered at all (a Production server does not map it).</summary>
        public Task<bool> ProbeTeleportAsync(CancellationToken ct)
        {
            using DevTeleport request = new() { MapId = GameMaps.Town, TileX = 1, TileY = 1 };
            return AnsweredAsync(() => _session.RequestAsync<DevTeleportResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct), ct);
        }

        public Task<bool> ProbeSeasonAsync(CancellationToken ct)
        {
            using DevSeason request = new() { Seasons = 1 };
            return AnsweredAsync(() => _session.RequestAsync<DevSeasonResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct), ct);
        }

        public Task<bool> ProbeFreezeClockAsync(CancellationToken ct)
        {
            using DevFreezeClock request = new() { Frozen = true };
            return AnsweredAsync(() => _session.RequestAsync<DevFreezeClockResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct), ct);
        }

        public Task<bool> ProbeSunnyDayAsync(CancellationToken ct)
        {
            using DevSunnyDay request = new();
            return AnsweredAsync(() => _session.RequestAsync<DevSunnyDayResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct), ct);
        }

        public Task<bool> ProbeLevelUpAsync(CancellationToken ct)
        {
            using DevLevelUp request = new() { Skill = 1, Level = 2 };
            return AnsweredAsync(() => _session.RequestAsync<DevLevelUpResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct), ct);
        }

        public Task<bool> ProbeBiteAsync(CancellationToken ct)
        {
            using DevFishingBite request = new() { CastId = 1 };
            return AnsweredAsync(() => _session.RequestAsync<DevFishingBiteResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct), ct);
        }

        private static async Task<bool> AnsweredAsync<T>(Func<ValueTask<T>> call, CancellationToken ct)
        {
            try
            {
                _ = await call();
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return false;
            }
        }

        public async Task SendMoveAsync(uint sequence, float x, float y, byte facing, CancellationToken ct)
        {
            TimeSpan wait = TimeSpan.FromMilliseconds(120) - (DateTime.UtcNow - _lastMove);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            _lastMove = DateTime.UtcNow;
            using MoveIntent intent = new() { Sequence = sequence, X = x, Y = y, Facing = facing, Moving = true };
            await _session.SendAsync(intent, ct);
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

        private static NodeRow Copy(NodeRow row) => new() { Id = row.Id, X = row.X, Y = row.Y, Kind = row.Kind, Variant = row.Variant, Stage = row.Stage, Hits = row.Hits };
    }
}
