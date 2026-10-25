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
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Private farms and farmhouses, public maps, the new maps' exits and player-placed furniture (docs/adr/ADR-015): real Nalix WebSocket
/// clients against the real host and a real PostgreSQL database.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class WorldInstanceTests : IClassFixture<PostgresFixture>
{
    private const byte Down = 0;

    // The first free slot after the starter kit: hoe, can, three seeds, axe, pickaxe, scythe.
    private const int ChairSlot = 8;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(40);

    private readonly PostgresFixture _database;

    public WorldInstanceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task TwoPlayers_OnTheirFarmsAndFarmhouses_NeverSeeEachOther()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client a = await host.ConnectAsync();
        await using Client b = await host.ConnectAsync();

        JoinMapResult joinA = await a.JoinAsync(host.Timeout);
        JoinMapResult joinB = await b.JoinAsync(host.Timeout);

        // Same map, different instances: each farm holds one player.
        joinA.MapId.Should().Be(GameMaps.Farm);
        joinB.MapId.Should().Be(GameMaps.Farm);
        joinA.SpawnX.Should().Be(joinB.SpawnX, "both stand at the same place of their own copy");
        (await a.WaitForSnapshotAsync(s => s.Contains(joinA.PlayerId), host.Timeout)).Players.Should().Equal(joinA.PlayerId);
        (await b.WaitForSnapshotAsync(s => s.Contains(joinB.PlayerId), host.Timeout)).Players.Should().Equal(joinB.PlayerId);
        for (int i = 0; i < 3; i++)
        {
            (await a.NextSnapshotAsync(host.Timeout)).Players.Should().NotContain(joinB.PlayerId);
            (await b.NextSnapshotAsync(host.Timeout)).Players.Should().NotContain(joinA.PlayerId);
        }

        // The server's own record agrees: two instances, one player each.
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        List<string> keys = [];
        presence.CopyActiveMapIds(keys);
        keys.Should().BeEquivalentTo(MapInstance.Of(GameMaps.Farm, a.AccountId).Key, MapInstance.Of(GameMaps.Farm, b.AccountId).Key);

        // A walks into their farmhouse; B, on their own farm, still sees only themselves.
        float doorFeetY = (15 * 16) + 15;
        await a.SendMoveAsync(1, joinA.SpawnX, doorFeetY, 2, host.Timeout);
        (await a.MapChanges.WaitAsync(host.Timeout)).MapId.Should().Be(GameMaps.FarmHouse);
        (await a.WaitForSnapshotAsync(s => s.MapChangesSeen >= 1, host.Timeout)).Players.Should().Equal(joinA.PlayerId);
        (await b.NextSnapshotAsync(host.Timeout)).Players.Should().Equal(joinB.PlayerId);

        // B walks into theirs: two farmhouses, never together.
        await b.SendMoveAsync(1, joinB.SpawnX, doorFeetY, 2, host.Timeout);
        (await b.MapChanges.WaitAsync(host.Timeout)).MapId.Should().Be(GameMaps.FarmHouse);
        (await b.WaitForSnapshotAsync(s => s.MapChangesSeen >= 1, host.Timeout)).Players.Should().Equal(joinB.PlayerId);
        (await a.NextSnapshotAsync(host.Timeout)).Players.Should().Equal(joinA.PlayerId);
    }

    [Fact]
    public async Task TwoPlayers_OnEveryPublicMap_SeeEachOther()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client a = await host.ConnectAsync();
        await using Client b = await host.ConnectAsync();

        foreach (string mapId in new[] { GameMaps.BusStop, GameMaps.Town, GameMaps.Forest, GameMaps.Backwoods })
        {
            await a.PutOnAsync(mapId);
            await b.PutOnAsync(mapId);
            await Task.Delay(1300, host.Timeout);
            JoinMapResult joinA = await a.JoinAsync(host.Timeout);
            JoinMapResult joinB = await b.JoinAsync(host.Timeout);

            joinA.MapId.Should().Be(mapId);
            joinB.MapId.Should().Be(mapId);
            (await a.WaitForSnapshotAsync(s => s.Contains(joinA.PlayerId) && s.Contains(joinB.PlayerId), host.Timeout)).Players.Should().HaveCount(2);
            (await b.WaitForSnapshotAsync(s => s.Contains(joinA.PlayerId) && s.Contains(joinB.PlayerId), host.Timeout)).Players.Should().HaveCount(2);
            IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
            List<PlayerPresence> here = [];
            presence.CopyPlayers(mapId, here);
            here.Should().HaveCount(2, "a public map is one instance keyed by its map id");
        }
    }

    [Theory]
    [InlineData(GameMaps.BusStop, GameMaps.Town)]
    [InlineData(GameMaps.Farm, GameMaps.Backwoods)]
    public async Task Move_OntoAnExitOfTheNewMaps_EntersTownAndBackwoods(string from, string to)
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client a = await host.ConnectAsync();
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(from, out MapDefinition? map);
        (float startX, float startY, float exitX, float exitY, byte facing) = ApproachToExit(map!, to);
        await a.PutOnAsync(from, startX, startY);
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        join.MapId.Should().Be(from);
        join.SpawnX.Should().Be(startX);

        await a.SendMoveAsync(1, exitX, exitY, facing, host.Timeout);

        MapChanged entered = await a.MapChanges.WaitAsync(host.Timeout);
        entered.MapId.Should().Be(to);
        entered.PlayerId.Should().Be(join.PlayerId);
        _ = maps.TryGet(to, out MapDefinition? target);
        Nosum.Gameplay.Movement.MoveValidator.FootBlocked(target!.Grid, entered.X, entered.Y).Should().BeFalse("the arrival point is walkable");
        (await a.WaitForSnapshotAsync(s => s.MapChangesSeen >= 1, host.Timeout)).Players.Should().Contain(join.PlayerId);
    }

    [Fact]
    public async Task Furniture_PlaceMoveAndPickUp_FollowsTheRules_PersistsAcrossARestart_AndIsPrivate()
    {
        string sessionToken;
        Guid accountId;
        int chairId;
        (int X, int Y) first;
        (int X, int Y) second;

        await using (TestHost host = await TestHost.StartAsync(_database))
        {
            await using Client a = await host.ConnectAsync();
            await using Client b = await host.ConnectAsync();
            sessionToken = a.SessionToken;
            accountId = a.AccountId;
            await a.PutOnAsync(GameMaps.FarmHouse);
            await b.PutOnAsync(GameMaps.FarmHouse);
            JoinMapResult joinA = await a.JoinAsync(host.Timeout);
            JoinMapResult joinB = await b.JoinAsync(host.Timeout);
            joinA.MapId.Should().Be(GameMaps.FarmHouse);
            (int X, int Y)[] free = FreeTilesNear(joinA.SpawnX, joinA.SpawnY).ToArray();
            first = free[0];
            second = free[1];

            // Nothing is placed yet; the tester grant gives two chairs, which land in the first free hotbar slot after the five tools and the seeds.
            (await a.GrantAsync(ItemCatalog.OakChair, 2, host.Timeout)).Should().BeTrue();
            _ = await a.WaitForSlotAsync(s => s.Slot == ChairSlot && s.Quantity == 2, host.Timeout); // the grant's own push, so later waits see only what follows
            (await a.GetPlacedAsync(host.Timeout)).Objects.Should().BeEmpty();
            (await a.GetInventoryAsync(host.Timeout)).Slots.Should().Contain(s => s.Slot == ChairSlot && s.ItemId == ItemCatalog.OakChair && s.Quantity == 2);

            // Refusals change nothing: the hoe, an empty slot, a rotation the chair has not got, a wall, the doorway, too far away.
            (await a.PlaceAsync(0, first.X, first.Y, 0, host.Timeout)).Status.Should().Be(PlaceStatus.NoItem);
            (await a.PlaceAsync(30, first.X, first.Y, 0, host.Timeout)).Status.Should().Be(PlaceStatus.NoItem);
            (await a.PlaceAsync(ChairSlot, first.X, first.Y, 4, host.Timeout)).Status.Should().Be(PlaceStatus.BadRotation);
            (await a.PlaceAsync(ChairSlot, 0, 0, 0, host.Timeout)).Status.Should().Be(PlaceStatus.Blocked);
            (await a.PlaceAsync(ChairSlot, 3, 10, 0, host.Timeout)).Status.Should().Be(PlaceStatus.Reserved);
            (await a.PlaceAsync(ChairSlot, -3, 4, 0, host.Timeout)).Status.Should().Be(PlaceStatus.OutsideRoom);
            (await a.PlaceAsync(ChairSlot, first.X + 9, first.Y, 0, host.Timeout)).Status.Should().BeOneOf(PlaceStatus.OutOfReach, PlaceStatus.OutsideRoom, PlaceStatus.Blocked);

            // Place a chair: the player is told, the instance is pushed the change, the item left the inventory.
            PlaceResult placed = await a.PlaceAsync(ChairSlot, first.X, first.Y, 1, host.Timeout);
            placed.Status.Should().Be(PlaceStatus.Ok);
            chairId = placed.ObjectId;
            chairId.Should().BeGreaterThan(0);
            PlacedChanged pushed = await a.PlacedChanges.WaitAsync(host.Timeout);
            pushed.MapId.Should().Be(GameMaps.FarmHouse);
            pushed.Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = chairId, ItemId = ItemCatalog.OakChair, TileX = (ushort)first.X, TileY = (ushort)first.Y, Rotation = (byte)1 });
            (await a.WaitForSlotAsync(s => s.Slot == ChairSlot, host.Timeout)).Quantity.Should().Be(1);

            // The same tile again overlaps; the chair now blocks walking on the server.
            (await a.PlaceAsync(ChairSlot, first.X, first.Y, 0, host.Timeout)).Status.Should().Be(PlaceStatus.Overlaps);
            await a.SendMoveAsync(1, (first.X * 16) + 8, (first.Y * 16) + 8, Down, host.Timeout);
            (await a.Corrections.WaitAsync(host.Timeout)).Reason.Should().Be(MoveRefusal.Blocked);

            // B stands in a farmhouse of their own: they see none of it, cannot touch it, and were pushed nothing.
            (await b.GetPlacedAsync(host.Timeout)).Objects.Should().BeEmpty();
            (await b.MoveAsync(chairId, first.X, first.Y, 0, host.Timeout)).Status.Should().Be(PlaceStatus.UnknownObject);
            (await b.PickUpAsync(chairId, host.Timeout)).Status.Should().Be(PlaceStatus.UnknownObject);
            await b.PlacedChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(400));

            // Move the chair, then pick it up: the item comes back and the removal is pushed.
            (await a.MoveAsync(chairId, second.X, second.Y, 2, host.Timeout)).Status.Should().Be(PlaceStatus.Ok);
            (await a.PlacedChanges.WaitAsync(host.Timeout)).Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = chairId, TileX = (ushort)second.X, TileY = (ushort)second.Y, Rotation = (byte)2 });
            (await a.PickUpAsync(chairId, host.Timeout)).Status.Should().Be(PlaceStatus.Ok);
            (await a.PlacedChanges.WaitAsync(host.Timeout)).Removed.Should().Equal(chairId);
            (await a.WaitForSlotAsync(s => s.Slot == ChairSlot && s.Quantity == 2, host.Timeout)).ItemId.Should().Be(ItemCatalog.OakChair);
            (await a.PickUpAsync(chairId, host.Timeout)).Status.Should().Be(PlaceStatus.UnknownObject);

            // Place it again for the restart below.
            PlaceResult again = await a.PlaceAsync(ChairSlot, second.X, second.Y, 3, host.Timeout);
            again.Status.Should().Be(PlaceStatus.Ok);
            chairId = again.ObjectId;
        }

        // The row is in PostgreSQL, owned by the instance of the player who placed it.
        await using (NosumDbContext context = new(_database.Options))
        {
            List<Nosum.Domain.Farm.PlacedObject> rows = context.PlacedObjects.Where(o => o.OwnerId == accountId).ToList();
            rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = chairId, MapId = GameMaps.FarmHouse, OwnerId = accountId, ItemId = ItemCatalog.OakChair, TileX = second.X, TileY = second.Y, Rotation = 3 });
        }

        // A fresh server (nothing in memory) and a new sign-in: the chair is still there, and still blocks walking.
        await using (TestHost restarted = await TestHost.StartAsync(_database))
        {
            await using Client again = await restarted.ConnectAsync(sessionToken);
            again.AccountId.Should().Be(accountId);
            JoinMapResult join = await again.JoinAsync(restarted.Timeout);
            join.MapId.Should().Be(GameMaps.FarmHouse, "the saved place is the player's own farmhouse");

            PlacedSnapshot snapshot = await again.GetPlacedAsync(restarted.Timeout);
            snapshot.MapId.Should().Be(GameMaps.FarmHouse);
            snapshot.Objects.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = chairId, ItemId = ItemCatalog.OakChair, TileX = (ushort)second.X, TileY = (ushort)second.Y, Rotation = (byte)3 });
            await again.SendMoveAsync(1, (second.X * 16) + 8, (second.Y * 16) + 8, Down, restarted.Timeout);
            (await again.Corrections.WaitAsync(restarted.Timeout)).Reason.Should().Be(MoveRefusal.Blocked);
        }
    }

    [Fact]
    public async Task CoopLand_MembersEnterThroughTheTownGate_AStrangerIsRefused_AKickedMemberIsWarpedOut()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client founder = await host.ConnectAsync();
        await using Client member = await host.ConnectAsync();
        await using Client stranger = await host.ConnectAsync();
        ICoopService coops = InstanceManager.Instance.GetExistingInstance<ICoopService>()!;
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        (await coops.CreateAsync(founder.AccountId, "Gate coop")).Should().Be(CoopStatus.Ok);
        Guid coopId;
        Guid memberRow;
        await using (NosumDbContext context = new(_database.Options))
        {
            coopId = (await context.CoopMembers.SingleAsync(m => m.PlayerId == founder.AccountId)).CoopId;
            CoopMember row = new() { CoopId = coopId, PlayerId = member.AccountId, Role = CoopRole.Member, JoinedAt = DateTimeOffset.UtcNow };
            context.CoopMembers.Add(row);
            _ = await context.SaveChangesAsync();
            memberRow = row.Id;
        }

        _ = maps.TryGet(GameMaps.Town, out MapDefinition? town);
        (float startX, float startY, float gateX, float gateY, byte facing) = ApproachToExit(town!, GameMaps.CoopFarm);
        Client[] all = [founder, member, stranger];
        Dictionary<Client, JoinMapResult> joins = [];
        foreach (Client client in all)
        {
            await client.PutOnAsync(GameMaps.Town, startX, startY);
            await Task.Delay(700, host.Timeout);
            joins[client] = await client.JoinAsync(host.Timeout);
            joins[client].MapId.Should().Be(GameMaps.Town);
        }

        await founder.SendMoveAsync(1, gateX, gateY, facing, host.Timeout);
        (await founder.MapChanges.WaitAsync(host.Timeout)).MapId.Should().Be(GameMaps.CoopFarm);
        await Task.Delay(700, host.Timeout);
        await member.SendMoveAsync(1, gateX, gateY, facing, host.Timeout);
        (await member.MapChanges.WaitAsync(host.Timeout)).MapId.Should().Be(GameMaps.CoopFarm);
        await Task.Delay(700, host.Timeout);
        await stranger.SendMoveAsync(1, gateX, gateY, facing, host.Timeout);
        await stranger.MapChanges.ExpectNothingAsync(TimeSpan.FromSeconds(1.5));

        string landKey = MapInstance.OfCoop(GameMaps.CoopFarm, coopId).Key;
        (await founder.WaitForSnapshotAsync(s => s.Contains(joins[founder].PlayerId) && s.Contains(joins[member].PlayerId), host.Timeout)).Players.Should().HaveCount(2, "members share one instance of the land");
        List<PlayerPresence> here = [];
        presence.CopyPlayers(landKey, here);
        here.Select(p => p.AccountId).Should().BeEquivalentTo([founder.AccountId, member.AccountId]);
        List<PlayerPresence> inTown = [];
        presence.CopyPlayers(GameMaps.Town, inTown);
        inTown.Select(p => p.AccountId).Should().Equal([stranger.AccountId], "the player without a co-op was refused at the gate and stayed in Town");
        inTown[0].Motion.X.Should().Be(gateX, "the stranger's step onto the gate was accepted; only the warp was refused");

        (await coops.KickAsync(founder.AccountId, memberRow)).Should().Be(CoopStatus.Ok);

        MapChanged evicted = await member.MapChanges.WaitAsync(host.Timeout);
        evicted.MapId.Should().Be(GameMaps.Town);
        evicted.PlayerId.Should().Be(joins[member].PlayerId);
        here.Clear();
        presence.CopyPlayers(landKey, here);
        here.Select(p => p.AccountId).Should().Equal([founder.AccountId], "the removed member is off the land at once");
        PlayerStateRow saved = (await InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!.LoadAsync(member.AccountId, CancellationToken.None))!.Value;
        saved.MapName.Should().Be(GameMaps.Town, "a kicked member who reconnects starts in Town, not on land they may not enter");
    }

    [Fact]
    public async Task Furniture_OutsideTheFarmhouse_IsRefused()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client a = await host.ConnectAsync();
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        join.MapId.Should().Be(GameMaps.Farm);
        (await a.GrantAsync(ItemCatalog.OakChair, 1, host.Timeout)).Should().BeTrue();

        (await a.PlaceAsync(ChairSlot, (int)(join.SpawnX / 16), (int)(join.SpawnY / 16) + 1, 0, host.Timeout)).Status.Should().Be(PlaceStatus.NotPlaceable);
        (await a.GetPlacedAsync(host.Timeout)).MapId.Should().BeEmpty("the farm takes no furniture");
    }

    [Fact]
    public async Task Furniture_BeforeSignIn_IsNotAnswered()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        using WebSocketSession plain = new(
            new TransportOptions { Address = "127.0.0.1", Port = host.Port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        await plain.ConnectAsync(ct: host.Timeout);
        await plain.HandshakeAsync(host.Timeout);
        using PlaceObject request = new() { Slot = 5, TileX = 4, TileY = 4 };

        Func<Task> act = async () => _ = await plain.RequestAsync<PlaceResult>(request, RequestOptions.Default.WithTimeout(1500), ct: host.Timeout);

        _ = await act.Should().ThrowAsync<Exception>();
    }

    // Walkable, unreserved tiles near a spawn that the player can reach, nearest first.
    private static IEnumerable<(int X, int Y)> FreeTilesNear(float feetX, float feetY)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.FarmHouse, out MapDefinition? house);
        int tx = (int)(feetX / 16);
        int ty = (int)(feetY / 16);
        return Enumerable.Range(-2, 5)
            .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
            .Where(t => !house!.Grid.IsBlocked(t.X, t.Y)
                && !house.ReservedTiles.Contains(t)
                && t != (tx, ty)
                && Nosum.Gameplay.Farm.FarmRules.InReach(feetX, feetY, t.X, t.Y, 16))
            .OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty));
    }

    // A start tile one step before an exit of the map to the target, the exit tile, and the facing to walk onto it.
    private static (float StartX, float StartY, float ExitX, float ExitY, byte Facing) ApproachToExit(MapDefinition map, string target)
    {
        foreach (MapWarp warp in map.Warps.Where(w => w.TargetMap == target))
        {
            if (!Nosum.Gameplay.Maps.WarpRule.TryEdge(warp, map.Grid, out int tx, out int ty, out int outward) || map.Grid.IsBlocked(tx, ty))
            {
                continue;
            }

            // An exit inside the map is used by standing on it: walk onto it from a free neighbour (any facing).
            (int dx, int dy) = outward switch { 0 => (0, 1), 1 => (1, 0), 2 => (0, -1), 3 => (-1, 0), _ => (1, 0) };
            if (outward < 0)
            {
                (int X, int Y) free = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }.Select(d => (X: tx + d.Item1, Y: ty + d.Item2)).FirstOrDefault(t => !map.Grid.IsBlocked(t.X, t.Y));
                if (map.Grid.IsBlocked(free.X, free.Y))
                {
                    continue;
                }

                (dx, dy) = (tx - free.X, ty - free.Y);
            }

            int sx = tx - dx;
            int sy = ty - dy;
            if (map.Grid.IsBlocked(sx, sy))
            {
                continue;
            }

            return ((sx * 16) + 8, (sy * 16) + 15, (tx * 16) + 8, (ty * 16) + 15, (byte)(outward < 0 ? 0 : outward));
        }

        throw new InvalidOperationException("No reachable edge exit from " + map.Id + " to " + target);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class TestHost : IAsyncDisposable
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

            // Development, so the tester grant can hand out the furniture that new players no longer start with.
            NetworkApplication app = Program.BuildApplication(port, null, development: true);
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

    private static class Pace
    {
        private static readonly SemaphoreSlim s_gate = new(1, 1);
        private static DateTime s_last = DateTime.MinValue;

        public static async Task WaitAsync(CancellationToken ct)
        {
            await s_gate.WaitAsync(ct);
            try
            {
                TimeSpan wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - s_last);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                s_last = DateTime.UtcNow;
            }
            finally
            {
                _ = s_gate.Release();
            }
        }
    }

    private sealed record Snapshot(List<uint> Players, int MapChangesSeen)
    {
        public bool Contains(uint playerId) => this.Players.Contains(playerId);
    }

    private sealed class Client : IAsyncDisposable
    {
        // The edit handlers allow a burst and then about three a second per address; tests share one address.
        private static readonly TimeSpan s_editCooldown = TimeSpan.FromMilliseconds(380);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private readonly BlockingCollection<Snapshot> _snapshots = [];
        private readonly BlockingCollection<InventorySlotRow> _slots = [];
        private DateTime _lastMove = DateTime.MinValue;
        private int _mapChanges;

        public Client(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<MapSnapshot>(s => _snapshots.Add(new Snapshot([.. s.Players.Select(p => p.PlayerId)], Volatile.Read(ref _mapChanges)))),
                session.On<MoveCorrection>(c => this.Corrections.Add(new MoveCorrection { Sequence = c.Sequence, Reason = c.Reason, X = c.X, Y = c.Y })),
                session.On<MapChanged>(m =>
                {
                    _ = Interlocked.Increment(ref _mapChanges);
                    this.MapChanges.Add(new MapChanged { PlayerId = m.PlayerId, MapId = m.MapId, X = m.X, Y = m.Y, Facing = m.Facing });
                }),
                session.On<PlacedChanged>(p =>
                {
                    PlacedChanged copy = new() { MapId = p.MapId, Removed = [.. p.Removed] };
                    foreach (PlacedObjectRow row in p.Upserted)
                    {
                        copy.Upserted.Add(new PlacedObjectRow { Id = row.Id, ItemId = row.ItemId, TileX = row.TileX, TileY = row.TileY, Rotation = row.Rotation });
                    }

                    this.PlacedChanges.Add(copy);
                }),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        _slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
            ];
        }

        public Inbox<MoveCorrection> Corrections { get; } = new();

        public Inbox<MapChanged> MapChanges { get; } = new();

        public Inbox<PlacedChanged> PlacedChanges { get; } = new();

        public Guid AccountId { get; private set; }

        public string SessionToken { get; private set; } = string.Empty;

        public async Task ConnectAsync(string? sessionToken, CancellationToken ct)
        {
            await _session.ConnectAsync(ct: ct);
            await _session.HandshakeAsync(ct);
            if (sessionToken is null)
            {
                // The account is created in the database directly: sign-ups are rate limited per address and every test of the suite shares one.
                IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
                AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
                created.Status.Should().Be(AccountStatus.Ok);
                sessionToken = created.SessionToken!;
            }

            // Resuming a session is limited to two a second per address.
            await Pace.WaitAsync(ct);
            using ResumeSession request = new() { SessionToken = sessionToken };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
            this.SessionToken = sessionToken;
        }

        // Saves where the player was last, so the server's join puts them there (a bad place falls back to that map's spawn).
        public async Task PutOnAsync(string mapId, float x = -1, float y = -1)
        {
            IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
            await states.SaveAsync(this.AccountId, new PlayerStateRow(mapId, x, y, Nosum.Core.Facing.Down), CancellationToken.None);
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            return result;
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

        public async Task<PlacedSnapshot> GetPlacedAsync(CancellationToken ct)
        {
            await Task.Delay(s_editCooldown, ct);
            using GetPlaced request = new();
            return await _session.RequestAsync<PlacedSnapshot>(request, ct: ct);
        }

        public async Task<InventorySnapshot> GetInventoryAsync(CancellationToken ct)
        {
            using GetInventory request = new();
            return await _session.RequestAsync<InventorySnapshot>(request, ct: ct);
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task<PlaceResult> PlaceAsync(int slot, int x, int y, int rotation, CancellationToken ct)
        {
            await Task.Delay(s_editCooldown, ct);
            using PlaceObject request = new() { Slot = (byte)slot, TileX = x, TileY = y, Rotation = (byte)rotation };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        public async Task<PlaceResult> MoveAsync(int objectId, int x, int y, int rotation, CancellationToken ct)
        {
            await Task.Delay(s_editCooldown, ct);
            using MoveObject request = new() { ObjectId = objectId, TileX = x, TileY = y, Rotation = (byte)rotation };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        public async Task<PlaceResult> PickUpAsync(int objectId, CancellationToken ct)
        {
            await Task.Delay(s_editCooldown, ct);
            using PickUpObject request = new() { ObjectId = objectId };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        public Task<InventorySlotRow> WaitForSlotAsync(Func<InventorySlotRow, bool> predicate, CancellationToken ct)
            => Task.Run(() =>
            {
                foreach (InventorySlotRow row in _slots.GetConsumingEnumerable(ct))
                {
                    if (predicate(row))
                    {
                        return row;
                    }
                }

                throw new InvalidOperationException("Inventory stream ended.");
            }, ct);

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
            _slots.Dispose();
        }
    }
}
