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
using Nosum.Abstractions.Economy;
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
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Blacksmith;
using Nosum.Domain.Farm;
using Nosum.Domain.Mines;
using Nosum.Domain.Nodes;
using Nosum.Domain.World;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Shops;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Visits;

namespace Nosum.IntegrationTests.Server;

public sealed partial class MineWireTests
{
    // The plan's nodes as the client sees them on the wire.
    internal static List<(ushort X, ushort Y, byte Kind)> Planned(MineFloorPlanner.Plan plan) => [.. plan.Nodes.Select(n => ((ushort)n.X, (ushort)n.Y, (byte)n.Kind))];

    // Puts the player on a floor beside the first node of that floor's plan that matches (the server builds the same plan from the same inputs), and joins.
    internal static async Task<(NodePlacement Node, MineFloorPlanner.Plan Plan)> StandNextToAsync(TestHost host, Client player, int floor, Func<NodePlacement, bool> match)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(MineRules.FloorMapId(floor), out MapDefinition? map);
        long today = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today;
        int spawnX = (int)(map!.SpawnX / 16);
        int spawnY = (int)((map.SpawnY - 1) / 16);
        MineFloorPlanner.Plan plan = MineFloorPlanner.Build(floor, MineRules.FloorSeed(player.AccountId, today, floor), map.Grid, map.ReservedTiles, spawnX, spawnY);
        HashSet<(int, int)> taken = [.. plan.Nodes.Select(n => (n.X, n.Y))];
        foreach (NodePlacement node in plan.Nodes.Where(match))
        {
            foreach ((int dx, int dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                int px = node.X + dx;
                int py = node.Y + dy;
                if (map.Grid.IsBlocked(px, py) || taken.Contains((px, py)) || map.ReservedTiles.Contains((px, py)))
                {
                    continue;
                }

                await player.PutOnAsync(map.Id, (px + 0.5f) * 16, ((py + 1) * 16) - 1);
                JoinMapResult join = await player.JoinAsync(host.Timeout);
                join.MapId.Should().Be(map.Id);
                join.SpawnX.Should().BeApproximately((px + 0.5f) * 16, 0.01f, "the player stands where the test put them, not at the arrival tile");
                return (node, plan);
            }
        }

        throw new InvalidOperationException("No node on floor " + floor + " has a free tile beside it.");
    }

    internal static async Task GrantAsync(Guid playerId, int itemId, int quantity, long coins)
    {
        ITransferService transfers = InstanceManager.Instance.GetExistingInstance<ITransferService>()!;
        (await transfers.ExecuteAsync([new TransferLeg(TransferParty.Player(playerId), itemId, quantity, coins)], "test.grant")).Should().Be(TransferStatus.Ok);
    }

    internal static Task<long> CoinsAsync(Guid playerId) => InstanceManager.Instance.GetExistingInstance<ITransferService>()!.GetBalanceAsync(playerId);

    internal static async Task<int> CountAsync(Guid playerId, int itemId)
        => (await InstanceManager.Instance.GetExistingInstance<IInventoryService>()!.GetAsync(playerId)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    // Replaces one tool by another in the same slot, as the smith's counter does.
    internal static async Task SwapAsync(Guid playerId, int oldItem, int newItem)
    {
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
        (await inventory.RemoveAsync(playerId, oldItem, 1, "test.swap")).IsOk.Should().BeTrue();
        (await inventory.AddAsync(playerId, newItem, 1, "test.swap")).IsOk.Should().BeTrue();
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    // The access policy of the host, built on the test database (the host keeps its own instance private).
    internal sealed class AccessPolicy
    {
        private readonly VisitAccess _access;

        public AccessPolicy(PostgresFixture database)
        {
            _ = database;
            _access = new VisitAccess(InstanceManager.Instance.GetExistingInstance<Nosum.Abstractions.Data.IUnitOfWorkFactory>()!);
        }

        public async Task<bool> CanEnterAsync(MapInstance instance, Guid playerId) => await _access.CanEnterAsync(instance, playerId);
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

        public static async Task<TestHost> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);

            // Development, so the tester clock can skip game days; the fake clock sits after the calendar epoch.
            NetworkApplication app = Program.BuildApplication(port, new FakeTimeProvider(s_start), development: true);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<Client> ConnectAsync(string? sessionToken = null)
        {
            Client client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = this.Port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(sessionToken, this.Timeout);
            return client;
        }

        // The first tile of a vertical line of the given length on the farm, in reach of the spawn, that can be tilled all the way down.
        public (int X, int Y) LineStart(JoinMapResult join, int length)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
            int tx = (int)(join.SpawnX / 16);
            int ty = (int)(join.SpawnY / 16);
            foreach ((int x, int y) in Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy))).OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty)))
            {
                bool lineOk = Enumerable.Range(0, length).All(i => farm!.Tillable!.IsTillable(x, y + i) && !farm.Grid.IsBlocked(x, y + i) && !farm.ReservedTiles.Contains((x, y + i)));
                if (lineOk && Nosum.Gameplay.Farm.FarmRules.InReach(join.SpawnX, join.SpawnY, x, y, 16) && !(x == tx && y == ty))
                {
                    return (x, y);
                }
            }

            throw new InvalidOperationException("No line of " + length + " tillable tiles near the farm's spawn.");
        }

        // A disconnect is noticed on the server's own time; floors are dropped when the last player has left them.
        public async Task WaitUntilNobodyIsOnAMapAsync()
        {
            Nosum.Abstractions.Presence.IMapPresence presence = InstanceManager.Instance.GetExistingInstance<Nosum.Abstractions.Presence.IMapPresence>()!;
            while (presence.PlayerCount > 0)
            {
                await Task.Delay(50, this.Timeout);
            }

            await Task.Delay(200, this.Timeout);
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

        // Nothing may arrive in the given time.
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

    internal sealed class Client : IAsyncDisposable
    {
        // The farm-use handler allows a burst and then eight a second per address; the server's swing cooldown is 300 ms.
        private static readonly TimeSpan s_swing = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;

        public Client(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<NodeChanged>(p => this.NodeChanges.Add(new NodeChanged { MapId = p.MapId, Upserted = [.. p.Upserted.Select(Copy)], Removed = [.. p.Removed] })),
                session.On<ToolState>(p => this.Tools.Add(new ToolState { WaterLeft = p.WaterLeft, WaterCapacity = p.WaterCapacity })),
                session.On<MapChanged>(p => this.MapChanges.Add(new MapChanged { PlayerId = p.PlayerId, MapId = p.MapId, X = p.X, Y = p.Y, Facing = p.Facing })),
                session.On<MapSnapshot>(p => this.Snapshots.Add(new MapSnapshot { ServerTimeMs = p.ServerTimeMs, Players = [.. p.Players.Select(s => new PlayerState { PlayerId = s.PlayerId, X = s.X, Y = s.Y, Facing = s.Facing, Moving = s.Moving })] })),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        this.Slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
            ];
        }

        public Inbox<NodeChanged> NodeChanges { get; } = new();

        public Inbox<ToolState> Tools { get; } = new();

        public Inbox<MapChanged> MapChanges { get; } = new();

        public Inbox<MapSnapshot> Snapshots { get; } = new();

        public Inbox<InventorySlotRow> Slots { get; } = new();

        public Guid AccountId { get; private set; }

        public uint MapPlayerId { get; private set; }

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

        // Saves where the player was last, so the server's join puts them there (a negative x means the map's own spawn).
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
            this.MapPlayerId = result.PlayerId;
            return result;
        }

        public async Task<NodeSnapshot> GetNodesAsync(CancellationToken ct)
        {
            await Task.Delay(250, ct);
            using GetNodes request = new();
            return await _session.RequestAsync<NodeSnapshot>(request, ct: ct);
        }

        public async Task<int> SlotOfAsync(int itemId)
        {
            using GetInventory request = new();
            InventorySnapshot snapshot = await _session.RequestAsync<InventorySnapshot>(request, ct: CancellationToken.None);
            return snapshot.Slots.First(s => s.ItemId == itemId).Slot;
        }

        // Waits out the swing cooldown first, like a player who keeps swinging.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        public async Task<UseItemResult> UseAreaAsync(int slot, int x, int y, int tiles, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            using UseItemArea request = new() { Slot = (byte)slot, TileX = x, TileY = y, Tiles = (byte)tiles };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        public async Task<BlacksmithState> GetBlacksmithAsync(CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using GetBlacksmith request = new();
            return await _session.RequestAsync<BlacksmithState>(request, ct: ct);
        }

        public async Task<BlacksmithState> UpgradeAsync(int itemId, CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using UpgradeTool request = new() { ItemId = itemId };
            return await _session.RequestAsync<BlacksmithState>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async Task<BlacksmithState> CollectAsync(CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using CollectTool request = new();
            return await _session.RequestAsync<BlacksmithState>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async Task<ShopResult> BuyAsync(int shopId, int itemId, int quantity, CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using BuyFromShop request = new() { ShopId = shopId, ItemId = itemId, Quantity = quantity };
            return await _session.RequestAsync<ShopResult>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
        }

        public async Task<MineState> GetMineAsync(CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using GetMine request = new();
            return await _session.RequestAsync<MineState>(request, ct: ct);
        }

        public async Task<MineState> RideAsync(int floor, CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using RideElevator request = new() { Floor = floor };
            return await _session.RequestAsync<MineState>(request, ct: ct);
        }

        public async Task<DevAdvanceClockResult> AdvanceClockAsync(int days, CancellationToken ct)
        {
            await WirePace.RequestAsync(ct);
            using DevAdvanceClock request = new() { Days = days };
            return await _session.RequestAsync<DevAdvanceClockResult>(request, RequestOptions.Default.WithEncrypt(), ct: ct);
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
