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
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Crafting, chests and furnaces (docs/adr/ADR-024) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database: craft a chest, place it
/// on the farm, open it, store items and take them back, and a furnace job that finishes when the server clock says so.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class CraftingWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;

    public CraftingWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task CraftAChest_PlaceItOnTheFarm_StoreItemsAndTakeThemBack()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        RecipeBook book = await player.RecipeBookAsync(host.Timeout);
        book.Unlocked.Should().Contain(RecipeCatalog.Chest);
        (await player.GrantAsync(ItemCatalog.Wood, 60, host.Timeout)).Should().BeTrue();
        (await player.GrantAsync(ItemCatalog.Stone, 40, host.Timeout)).Should().BeTrue();

        // Craft: one request, one result, the wood replaced by a chest.
        CraftResult crafted = await player.CraftAsync(RecipeCatalog.Chest, 1, host.Timeout);
        crafted.Should().BeEquivalentTo(new { Status = CraftStatus.Ok, RecipeId = (ushort)RecipeCatalog.Chest, Times = (byte)1 });
        InventorySnapshot bag = await player.InventoryAsync(host.Timeout);
        bag.Slots.Should().Contain(s => s.ItemId == ItemCatalog.Chest && s.Quantity == 1);
        bag.Slots.Where(s => s.ItemId == ItemCatalog.Wood).Sum(s => s.Quantity).Should().Be(10);
        (await player.CraftAsync(RecipeCatalog.Chest, 1, host.Timeout)).Status.Should().Be(CraftStatus.NotEnough, "ten wood are not enough for another");

        // Place it outdoors, next to the spawn.
        (int x, int y) = host.FreeTilesNear(GameMaps.Farm)[0];
        int chestSlot = bag.Slots.First(s => s.ItemId == ItemCatalog.Chest).Slot;
        PlaceResult placed = await player.PlaceAsync(chestSlot, x, y, host.Timeout);
        placed.Status.Should().Be(PlaceStatus.Ok);
        PlacedChanged pushed = await player.PlacedChanges.WaitAsync(c => c.Upserted.Any(u => u.Id == placed.ObjectId), host.Timeout);
        pushed.Upserted.Single(u => u.Id == placed.ObjectId).Should().BeEquivalentTo(new { ItemId = ItemCatalog.Chest, TileX = (ushort)x, TileY = (ushort)y });
        PlacedSnapshot snapshot = await player.PlacedAsync(host.Timeout);
        snapshot.Objects.Should().ContainSingle(o => o.Id == placed.ObjectId);

        // Open it: empty, thirty-six slots.
        ChestSnapshot opened = await player.OpenChestAsync(placed.ObjectId, host.Timeout);
        opened.Status.Should().Be(ChestStatus.Ok);
        (opened.Capacity, opened.Slots.Count).Should().Be(((byte)36, 0));

        // Store the stone: the whole stack into slot 4, then take part of it back into the first free bag slot.
        int stoneSlot = (await player.InventoryAsync(host.Timeout)).Slots.First(s => s.ItemId == ItemCatalog.Stone).Slot;
        (await player.ChestMoveAsync(placed.ObjectId, ChestSide.Player, stoneSlot, ChestSide.Chest, 4, 0, host.Timeout)).Status.Should().Be(ChestStatus.Ok);
        ChestUpdate stored = await player.ChestUpdates.WaitAsync(u => u.Slots.Any(s => s.ItemId == ItemCatalog.Stone), host.Timeout);
        stored.Slots.Single(s => s.Slot == 4).Quantity.Should().Be(40);
        (await player.InventoryAsync(host.Timeout)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Stone);

        (await player.ChestMoveAsync(placed.ObjectId, ChestSide.Chest, 4, ChestSide.Player, byte.MaxValue, 15, host.Timeout)).Status.Should().Be(ChestStatus.Ok);
        (await player.InventoryAsync(host.Timeout)).Slots.Where(s => s.ItemId == ItemCatalog.Stone).Sum(s => s.Quantity).Should().Be(15);
        (await player.OpenChestAsync(placed.ObjectId, host.Timeout)).Slots.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Slot = (byte)4, ItemId = ItemCatalog.Stone, Quantity = 25 });

        // A chest with something inside stays where it is; once emptied it can be picked up and is a chest in the bag again.
        (await player.PickUpAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(PlaceStatus.NotEmpty);
        (await player.ChestMoveAsync(placed.ObjectId, ChestSide.Chest, 4, ChestSide.Player, byte.MaxValue, 0, host.Timeout)).Status.Should().Be(ChestStatus.Ok);
        (await player.PickUpAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(PlaceStatus.Ok);
        (await player.ChestClosed.WaitAsync(c => c.ObjectId == placed.ObjectId, host.Timeout)).ObjectId.Should().Be(placed.ObjectId);
        (await player.InventoryAsync(host.Timeout)).Slots.Should().Contain(s => s.ItemId == ItemCatalog.Chest);
        await using NosumDbContext db = new(_database.Options);
        (await db.ChestLog.Where(e => e.ObjectId == placed.ObjectId).Select(e => e.Delta).ToListAsync()).Sum().Should().Be(0, "everything that went in came out");
    }

    [Fact]
    public async Task Chest_AnotherPlayersChest_AndANumberThatIsNoChest_AreRefused()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client owner = await host.ConnectAsync();
        _ = await owner.JoinAsync(host.Timeout);
        (await owner.GrantAsync(ItemCatalog.Chest, 1, host.Timeout)).Should().BeTrue();
        (int x, int y) = host.FreeTilesNear(GameMaps.Farm)[0];
        int slot = (await owner.InventoryAsync(host.Timeout)).Slots.First(s => s.ItemId == ItemCatalog.Chest).Slot;
        PlaceResult placed = await owner.PlaceAsync(slot, x, y, host.Timeout);
        placed.Status.Should().Be(PlaceStatus.Ok);

        await using Client other = await host.ConnectAsync();
        _ = await other.JoinAsync(host.Timeout);

        (await other.OpenChestAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(ChestStatus.UnknownObject, "the chest is in the owner's farm, not in this player's");
        (await other.OpenChestAsync(int.MaxValue, host.Timeout)).Status.Should().Be(ChestStatus.UnknownObject);
        (await other.ChestMoveAsync(placed.ObjectId, ChestSide.Chest, 0, ChestSide.Player, byte.MaxValue, 0, host.Timeout)).Status.Should().Be(ChestStatus.UnknownObject);
        (await other.PickUpAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(PlaceStatus.UnknownObject);
        (await owner.OpenChestAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(ChestStatus.Ok);
    }

    [Fact]
    public async Task Furnace_CraftPlaceSmeltAndCollect_AfterThirtyGameMinutes()
    {
        FakeTimeProvider clock = new(s_start);
        await using TestHost host = await TestHost.StartAsync(_database, clock);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (await player.GrantAsync(ItemCatalog.CopperOre, 25, host.Timeout)).Should().BeTrue();
        (await player.GrantAsync(ItemCatalog.Stone, 25, host.Timeout)).Should().BeTrue();
        (await player.GrantAsync(ItemCatalog.Coal, 2, host.Timeout)).Should().BeTrue();

        (await player.CraftAsync(RecipeCatalog.Furnace, 1, host.Timeout)).Status.Should().Be(CraftStatus.Ok);
        (int x, int y) = host.FreeTilesNear(GameMaps.Farm)[0];
        int slot = (await player.InventoryAsync(host.Timeout)).Slots.First(s => s.ItemId == ItemCatalog.Furnace).Slot;
        PlaceResult placed = await player.PlaceAsync(slot, x, y, host.Timeout);
        placed.Status.Should().Be(PlaceStatus.Ok);

        // Twenty of the twenty-five ore went into the furnace itself: five are left, and no iron.
        (await player.StartSmeltingAsync(placed.ObjectId, ItemCatalog.IronOre, host.Timeout)).Status.Should().Be(FurnaceStatus.NotEnough);
        (await player.StartSmeltingAsync(placed.ObjectId, ItemCatalog.Stone, host.Timeout)).Status.Should().Be(FurnaceStatus.NotSmeltable);
        FurnaceResult started = await player.StartSmeltingAsync(placed.ObjectId, ItemCatalog.CopperOre, host.Timeout);
        started.Status.Should().Be(FurnaceStatus.Ok);
        FurnaceChanged working = await player.FurnaceChanges.WaitAsync(c => c.ObjectId == placed.ObjectId && c.State == FurnaceStateCode.Working, host.Timeout);
        working.RemainingMs.Should().Be(15 * 60 * 1000, "thirty game minutes at the default day length");
        FurnaceSnapshot snapshot = await player.FurnacesAsync(host.Timeout);
        snapshot.Furnaces.Should().ContainSingle().Which.State.Should().Be(FurnaceStateCode.Working);

        (await player.CollectFurnaceAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(FurnaceStatus.NotReady);
        (await player.PickUpAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(PlaceStatus.InUse);

        clock.Advance(TimeSpan.FromMinutes(15));
        FurnaceResult collected = await player.CollectFurnaceAsync(placed.ObjectId, host.Timeout);
        collected.Should().BeEquivalentTo(new { Status = FurnaceStatus.Ok, ObjectId = placed.ObjectId, ItemId = ItemCatalog.CopperBar, Quantity = 1 });
        (await player.InventoryAsync(host.Timeout)).Slots.Should().Contain(s => s.ItemId == ItemCatalog.CopperBar && s.Quantity == 1);
        (await player.FurnaceChanges.WaitAsync(c => c.State == FurnaceStateCode.Idle, host.Timeout)).ObjectId.Should().Be(placed.ObjectId);
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

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider? clock = null)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);

            // Development, so the tester grant can hand out wood, stone and ore.
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<Client> ConnectAsync()
        {
            Client client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = this.Port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(this.Timeout);
            return client;
        }

        // Walkable, unreserved tiles around the map's spawn, nearest first, that are within reach of the spawn feet and not under them.
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

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }

    private static class Pace
    {
        private static readonly SemaphoreSlim s_gate = new(1, 1);
        private static DateTime s_last = DateTime.MinValue;

        // Resuming a session is limited to two a second per address, and every test of the suite shares one.
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

    // Pushes arrive on socket threads; tests read them in order and skip what does not match.
    private sealed class Inbox<T>
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
    }

    private sealed class Client : IAsyncDisposable
    {
        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;

        public Client(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<PlacedChanged>(p => this.PlacedChanges.Add(new PlacedChanged { MapId = p.MapId, Upserted = [.. p.Upserted.Select(r => new PlacedObjectRow { Id = r.Id, ItemId = r.ItemId, TileX = r.TileX, TileY = r.TileY, Rotation = r.Rotation })], Removed = [.. p.Removed] })),
                session.On<ChestUpdate>(p => this.ChestUpdates.Add(new ChestUpdate { ObjectId = p.ObjectId, Slots = [.. p.Slots.Select(s => new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity })] })),
                session.On<ChestClosed>(p => this.ChestClosed.Add(new ChestClosed { ObjectId = p.ObjectId })),
                session.On<FurnaceChanged>(p => this.FurnaceChanges.Add(new FurnaceChanged { ObjectId = p.ObjectId, State = p.State, OreItemId = p.OreItemId, RemainingMs = p.RemainingMs })),
            ];
        }

        public Inbox<PlacedChanged> PlacedChanges { get; } = new();

        public Inbox<ChestUpdate> ChestUpdates { get; } = new();

        public Inbox<ChestClosed> ChestClosed { get; } = new();

        public Inbox<FurnaceChanged> FurnaceChanges { get; } = new();

        public Guid AccountId { get; private set; }

        public async Task ConnectAsync(CancellationToken ct)
        {
            await _session.ConnectAsync(ct: ct);
            await _session.HandshakeAsync(ct);

            // The account is created in the database directly: sign-ups are rate limited per address.
            IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
            AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
            created.Status.Should().Be(AccountStatus.Ok);
            await Pace.WaitAsync(ct);
            using ResumeSession request = new() { SessionToken = created.SessionToken! };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            return result;
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task<InventorySnapshot> InventoryAsync(CancellationToken ct)
        {
            await Task.Delay(260, ct);
            using GetInventory request = new();
            return await _session.RequestAsync<InventorySnapshot>(request, ct: ct);
        }

        public async Task<RecipeBook> RecipeBookAsync(CancellationToken ct)
        {
            using GetRecipeBook request = new();
            return await _session.RequestAsync<RecipeBook>(request, ct: ct);
        }

        public async Task<CraftResult> CraftAsync(int recipeId, int times, CancellationToken ct)
        {
            await Task.Delay(170, ct);
            using Craft request = new() { RecipeId = (ushort)recipeId, Times = (byte)times };
            return await _session.RequestAsync<CraftResult>(request, ct: ct);
        }

        public async Task<PlaceResult> PlaceAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(130, ct);
            using PlaceObject request = new() { Slot = (byte)slot, TileX = x, TileY = y, Rotation = 0 };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        public async Task<PlaceResult> PickUpAsync(int objectId, CancellationToken ct)
        {
            await Task.Delay(130, ct);
            using PickUpObject request = new() { ObjectId = objectId };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        public async Task<PlacedSnapshot> PlacedAsync(CancellationToken ct)
        {
            await Task.Delay(260, ct);
            using GetPlaced request = new();
            return await _session.RequestAsync<PlacedSnapshot>(request, ct: ct);
        }

        public async Task<ChestSnapshot> OpenChestAsync(int objectId, CancellationToken ct)
        {
            await Task.Delay(170, ct);
            using OpenChest request = new() { ObjectId = objectId };
            return await _session.RequestAsync<ChestSnapshot>(request, ct: ct);
        }

        public async Task<ChestResult> ChestMoveAsync(int objectId, ChestSide from, int fromSlot, ChestSide to, int toSlot, int quantity, CancellationToken ct)
        {
            await Task.Delay(90, ct);
            using ChestMove request = new() { ObjectId = objectId, FromSide = from, FromSlot = (byte)fromSlot, ToSide = to, ToSlot = (byte)toSlot, Quantity = (ushort)quantity };
            return await _session.RequestAsync<ChestResult>(request, ct: ct);
        }

        public async Task<FurnaceResult> StartSmeltingAsync(int objectId, int oreItemId, CancellationToken ct)
        {
            await Task.Delay(170, ct);
            using StartSmelting request = new() { ObjectId = objectId, OreItemId = oreItemId };
            return await _session.RequestAsync<FurnaceResult>(request, ct: ct);
        }

        public async Task<FurnaceResult> CollectFurnaceAsync(int objectId, CancellationToken ct)
        {
            await Task.Delay(170, ct);
            using CollectFurnace request = new() { ObjectId = objectId };
            return await _session.RequestAsync<FurnaceResult>(request, ct: ct);
        }

        public async Task<FurnaceSnapshot> FurnacesAsync(CancellationToken ct)
        {
            await Task.Delay(260, ct);
            using GetFurnaces request = new();
            return await _session.RequestAsync<FurnaceSnapshot>(request, ct: ct);
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
    }
}
