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
using Nalix.Abstractions.Networking;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;
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
using Nosum.Contracts.World.S2C;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Shops;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Farm buildings and animals (docs/adr/ADR-028) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database, with the development clock moved by whole
/// game days: commission a coop at the carpenter, place it on the farm, wait two game days, buy a hen, walk through the door, feed it, find the egg the next day, and collect it exactly
/// once however many requests ask; a visitor the owner allowed may walk in and look, but cannot collect.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class BuildingWireTests : IClassFixture<PostgresFixture>
{
    private static readonly long s_dayMs = Nosum.Gameplay.Calendar.GameCalendar.DefaultDayLengthMs;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(120);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;

    public BuildingWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Coop_CommissionBuildWaitBuyFeedAndCollect_AVisitorMayLookButNotTake()
    {
        FakeTimeProvider clock = new(s_start);
        await using TestHost host = await TestHost.StartAsync(_database, clock);
        BuildingDefinition coop = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Coop);
        MapSpec town = GameMaps.All.Single(m => m.Id == GameMaps.Town);
        ShopPoint counter = town.Shops.Single(p => p.ShopId == ShopCatalog.CarpenterShop);

        // The owner starts at the carpenter's counter in Town.
        await using Client owner = await host.ConnectAsync();
        await owner.PutOnAsync(GameMaps.Town, (counter.TileX * 16) + 8, (counter.TileY * 16) + 8);
        _ = await owner.JoinAsync(host.Timeout);
        await host.MakeRegisteredAsync(owner.AccountId);
        (await owner.GrantAsync(0, 0, 6_000, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Wood, 99, 0, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Wood, 99, 0, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Wood, 99, 0, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Wood, 3, 0, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Stone, 99, 0, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Stone, 2, 0, host.Timeout)).Should().BeTrue();
        (await owner.GrantAsync(ItemCatalog.Hay, 6, 0, host.Timeout)).Should().BeTrue();

        // Commission: coins, wood and stone become a blueprint in one transaction.
        BuildingResult commissioned = await owner.CommissionAsync(BuildingKind.Coop, host.Timeout);
        commissioned.Should().BeEquivalentTo(new { Status = BuildingStatus.Ok, ItemId = ItemCatalog.CoopBlueprint, Quantity = 1 });
        InventorySnapshot bag = await owner.InventoryAsync(host.Timeout);
        bag.Slots.Should().Contain(s => s.ItemId == ItemCatalog.CoopBlueprint);
        bag.Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Wood, "all three hundred went into the blueprint");
        (await owner.CommissionAsync(BuildingKind.Coop, host.Timeout)).Status.Should().Be(BuildingStatus.NotEnoughItems, "the wood is gone");

        // Home, and onto the farm: the blueprint is set down like a large piece, in front of the farmhouse.
        await host.TravelAsync(owner.AccountId, new MapInstance(GameMaps.Farm, owner.AccountId));
        (await owner.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout)).MapId.Should().Be(GameMaps.Farm);
        int blueprintSlot = (await owner.InventoryAsync(host.Timeout)).Slots.First(s => s.ItemId == ItemCatalog.CoopBlueprint).Slot;
        (PlaceResult placed, int tileX, int tileY) = await owner.PlaceBuildingAsync(host, blueprintSlot, coop, host.Timeout);
        placed.Status.Should().Be(PlaceStatus.Ok);
        BuildingsSnapshot announced = await owner.BuildingPushes.WaitAsync(b => b.Buildings.Any(r => r.ObjectId == placed.ObjectId), host.Timeout);
        BuildingRow row = announced.Buildings.Single(r => r.ObjectId == placed.ObjectId);
        (row.Kind, row.MapId, row.Capacity).Should().Be(((byte)BuildingKind.Coop, GameMaps.Farm, (byte)4));
        row.RemainingMs.Should().BeInRange((int)(2 * s_dayMs) - 5_000, (int)(2 * s_dayMs));
        (await owner.MoveBuildingAsync(placed.ObjectId, tileX + 1, tileY, host.Timeout)).Status.Should().Be(PlaceStatus.Fixed);
        (await owner.PickUpAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(PlaceStatus.Fixed);

        // Under construction, the door does nothing: stepping on it leaves the player where they are.
        await host.PutAsync(owner.AccountId, (tileX + coop.DoorX) * 16 + 8, ((tileY + coop.DoorY + 1) * 16) + 4, Facing.Up);
        await owner.StepAsync(host, 1, (tileX + coop.DoorX) * 16 + 8, ((tileY + coop.DoorY) * 16) + 12, Facing.Up, host.Timeout);
        (await host.MapOf(owner.AccountId)).Should().Be(GameMaps.Farm, "the coop is not finished");

        // Two game days later it is.
        (await owner.AdvanceDaysAsync(2, host.Timeout)).Should().BeTrue();
        (await owner.BuildingsAsync(mine: true, host.Timeout)).Buildings.Should().ContainSingle().Which.RemainingMs.Should().Be(0);
        Guid buildingId = (await owner.BuildingsAsync(mine: true, host.Timeout)).Buildings.Single().Id;

        // A hen from the counter, named; three more coins than it needs left over.
        await host.TravelAsync(owner.AccountId, MapInstance.Shared(GameMaps.Town));
        _ = await owner.MapChanges.WaitAsync(m => m.MapId == GameMaps.Town, host.Timeout);
        await host.PutAsync(owner.AccountId, (counter.TileX * 16) + 8, (counter.TileY * 16) + 8, Facing.Down);
        (await owner.BuyAnimalAsync(buildingId, AnimalKind.Cow, "Rosie", host.Timeout)).Status.Should().Be(BuildingStatus.WrongResident);
        (await owner.BuyAnimalAsync(buildingId, AnimalKind.Chicken, "x", host.Timeout)).Status.Should().Be(BuildingStatus.BadName);
        BuildingResult bought = await owner.BuyAnimalAsync(buildingId, AnimalKind.Chicken, "Bella", host.Timeout);
        bought.Status.Should().Be(BuildingStatus.Ok);
        Guid hen = bought.Id;

        // Back on the farm, walk onto the door: the coop's inside.
        await host.TravelAsync(owner.AccountId, new MapInstance(GameMaps.Farm, owner.AccountId));
        _ = await owner.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        await host.PutAsync(owner.AccountId, (tileX + coop.DoorX) * 16 + 8, ((tileY + coop.DoorY + 1) * 16) + 4, Facing.Up);
        await owner.StepAsync(host, 2, (tileX + coop.DoorX) * 16 + 8, ((tileY + coop.DoorY) * 16) + 12, Facing.Up, host.Timeout);
        MapChanged inside = await owner.MapChanges.WaitAsync(m => m.MapId == GameMaps.Coop, host.Timeout);
        inside.MapId.Should().Be(GameMaps.Coop);
        AnimalsSnapshot room = await owner.AnimalsAsync(host.Timeout);
        (room.BuildingId, room.Kind, room.MayTend, room.Rations).Should().Be((buildingId, (byte)BuildingKind.Coop, true, 0));
        room.Animals.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = hen, Name = "Bella", Kind = (byte)AnimalKind.Chicken, Friendship = 0, Flags = (byte)0 });

        // Feed it: stand at the bench with hay in hand.
        await host.PutAsync(owner.AccountId, ((coop.TroughLeft + 1) * 16) + 8, ((coop.TroughTop + 1) * 16) + 8, Facing.Up);
        int haySlot = (await owner.InventoryAsync(host.Timeout)).Slots.First(s => s.ItemId == ItemCatalog.Hay).Slot;
        (await owner.FeedAsync(haySlot, 4, host.Timeout)).Should().BeEquivalentTo(new { Status = BuildingStatus.Ok, ItemId = ItemCatalog.Hay, Quantity = 4 });
        (await owner.AnimalChanges.WaitAsync(c => c.Rations == 4, host.Timeout)).BuildingId.Should().Be(buildingId);

        // The next day the hen has laid an egg. A visitor the owner allowed may walk in and look, but not take it.
        (await owner.AdvanceDaysAsync(1, host.Timeout)).Should().BeTrue();
        await using Client visitor = await host.ConnectAsync();
        _ = await visitor.JoinAsync(host.Timeout);
        await host.AllowVisitAsync(owner.AccountId, visitor.AccountId);
        await host.TravelAsync(visitor.AccountId, new MapInstance(GameMaps.Farm, owner.AccountId));
        _ = await visitor.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        await host.PutAsync(visitor.AccountId, (tileX + coop.DoorX) * 16 + 8, ((tileY + coop.DoorY + 1) * 16) + 4, Facing.Up);
        await visitor.StepAsync(host, 3, (tileX + coop.DoorX) * 16 + 8, ((tileY + coop.DoorY) * 16) + 12, Facing.Up, host.Timeout);
        _ = await visitor.MapChanges.WaitAsync(m => m.MapId == GameMaps.Coop, host.Timeout);
        AnimalsSnapshot looked = await visitor.AnimalsAsync(host.Timeout);
        looked.MayTend.Should().BeFalse();
        looked.Animals.Should().ContainSingle().Which.Flags.Should().Be(AnimalView.ProduceFlag);
        await host.PutNearAnimalAsync(visitor.AccountId, hen);
        (await visitor.CollectAsync(hen, 0, host.Timeout)).Status.Should().Be(BuildingStatus.NotYours);
        (await visitor.PetAsync(hen, host.Timeout)).Status.Should().Be(BuildingStatus.NotYours);

        // The owner collects: asked three times at once, the egg is handed over once.
        await host.PutNearAnimalAsync(owner.AccountId, hen);
        AnimalResult[] results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => owner.CollectAsync(hen, 0, host.Timeout)));
        results.Count(r => r.Status == BuildingStatus.Ok).Should().Be(1);
        results.Where(r => r.Status != BuildingStatus.Ok).Should().OnlyContain(r => r.Status == BuildingStatus.NoProduce);
        AnimalResult egg = results.Single(r => r.Status == BuildingStatus.Ok);
        egg.ItemId.Should().BeOneOf(ItemCatalog.Egg, ItemCatalog.LargeEgg);
        (await owner.InventoryAsync(host.Timeout)).Slots.Where(s => s.ItemId == egg.ItemId).Sum(s => s.Quantity).Should().Be(1);
        (await owner.PetAsync(hen, host.Timeout)).Should().BeEquivalentTo(new { Status = BuildingStatus.Ok, Friendship = AnimalRules.PettingGain });

        // Out through the doorway of the interior: back on the farm, in front of the door.
        await host.PutAsync(owner.AccountId, (coop.ArrivalX * 16) + 8, (coop.ArrivalY * 16) + 14, Facing.Down);
        await owner.StepAsync(host, 4, (coop.ArrivalX * 16) + 8, ((coop.ArrivalY + 1) * 16) + 4, Facing.Down, host.Timeout);
        MapChanged outside = await owner.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        (WarpRule.TileX(outside.X, 16), WarpRule.TileY(outside.Y, 16)).Should().Be((tileX + coop.DoorX, tileY + coop.DoorY + 1), "the farm, on the tile in front of the door");
        (await host.MapOf(owner.AccountId)).Should().Be(GameMaps.Farm);
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
        private readonly PostgresFixture _database;

        private TestHost(NetworkApplication app, ushort port, PostgresFixture database)
        {
            _app = app;
            _database = database;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider clock)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);

            // Development: the tester tools hand out coins and items and move the game clock by whole days.
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            TestHost host = new(app, port, database);
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

        // The server's own record of a player: where they stand, on what map.
        private PlayerPresence PresenceOf(Guid account)
        {
            ISessionRegistry sessions = InstanceManager.Instance.GetExistingInstance<ISessionRegistry>()!;
            IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
            IConnection connection = sessions.GetActive(account)!;
            _ = presence.TryGet(connection, out PlayerPresence? player).Should().BeTrue();
            return player!;
        }

        public Task<string> MapOf(Guid account) => Task.FromResult(this.PresenceOf(account).MapId);

        // Carries a player to a place the way a visit or a warp does.
        public async Task TravelAsync(Guid account, MapInstance destination)
        {
            ISessionRegistry sessions = InstanceManager.Instance.GetExistingInstance<ISessionRegistry>()!;
            IPlayerService players = InstanceManager.Instance.GetExistingInstance<IPlayerService>()!;
            (await players.TravelAsync(sessions.GetActive(account)!, destination)).Should().Be(TravelStatus.Arrived);
        }

        // Stands a player at a point of the place they are in, as if they had walked there.
        public Task PutAsync(Guid account, double x, double y, Facing facing)
        {
            PlayerPresence player = this.PresenceOf(account);
            double now = TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency;
            lock (player.Gate)
            {
                player.Motion = new MotionState(x, y, now, (int)facing);
            }

            return Task.CompletedTask;
        }

        // Stands a player on an animal's feet.
        public Task PutNearAnimalAsync(Guid account, Guid animal)
        {
            IAnimalWander wander = InstanceManager.Instance.GetExistingInstance<IAnimalWander>()!;
            long nowMs = InstanceManager.Instance.GetExistingInstance<Nosum.Abstractions.Calendar.ICalendarService>()!.NowMs();
            AnimalMotionView where = wander.Where(BuildingKind.Coop, animal, nowMs);
            return this.PutAsync(account, where.X, where.Y, Facing.Down);
        }

        // Sign-ups of real accounts are rate limited per address: a guest becomes a registered player by editing the row.
        public async Task MakeRegisteredAsync(Guid account)
        {
            await using NosumDbContext db = new(_database.Options);
            _ = await db.Players.Where(p => p.Id == account).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsGuest, false));
        }

        public async Task AllowVisitAsync(Guid owner, Guid visitor)
        {
            await using NosumDbContext db = new(_database.Options);
            db.VisitGrants.Add(new VisitGrant { OwnerId = owner, VisitorId = visitor, CreatedAt = s_start });
            _ = await db.SaveChangesAsync();
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
                session.On<MapChanged>(m => this.MapChanges.Add(new MapChanged { PlayerId = m.PlayerId, MapId = m.MapId, X = m.X, Y = m.Y, Facing = m.Facing })),
                session.On<BuildingsSnapshot>(b => this.BuildingPushes.Add(new BuildingsSnapshot { Mine = b.Mine, Buildings = [.. b.Buildings.Select(r => new BuildingRow { Id = r.Id, ObjectId = r.ObjectId, Kind = r.Kind, MapId = r.MapId, RemainingMs = r.RemainingMs, Animals = r.Animals, Capacity = r.Capacity })] })),
                session.On<AnimalsChanged>(a => this.AnimalChanges.Add(new AnimalsChanged { BuildingId = a.BuildingId, Rations = a.Rations, Upserted = [.. a.Upserted.Select(r => new AnimalRow { Id = r.Id, Kind = r.Kind, Name = r.Name, Friendship = r.Friendship, Flags = r.Flags })], Removed = [.. a.Removed] })),
            ];
        }

        public Inbox<MapChanged> MapChanges { get; } = new();

        public Inbox<BuildingsSnapshot> BuildingPushes { get; } = new();

        public Inbox<AnimalsChanged> AnimalChanges { get; } = new();

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

        // Saves where the player was last, so the server's join puts them there.
        public async Task PutOnAsync(string mapId, float x, float y)
        {
            IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
            await states.SaveAsync(this.AccountId, new PlayerStateRow(mapId, x, y, Facing.Down), CancellationToken.None);
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            return result;
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, long coins, CancellationToken ct)
        {
            await Task.Delay(120, ct);
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity, Coins = coins };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task<bool> AdvanceDaysAsync(int days, CancellationToken ct)
        {
            await Task.Delay(300, ct);
            using DevAdvanceClock request = new() { Days = days };
            return (await _session.RequestAsync<DevAdvanceClockResult>(request, ct: ct)).Ok;
        }

        public async Task<InventorySnapshot> InventoryAsync(CancellationToken ct)
        {
            await Task.Delay(300, ct);
            using GetInventory request = new();
            return await _session.RequestAsync<InventorySnapshot>(request, ct: ct);
        }

        public async Task<BuildingResult> CommissionAsync(BuildingKind kind, CancellationToken ct)
        {
            await Task.Delay(400, ct);
            using CommissionBuilding request = new() { Kind = (byte)kind };
            return await _session.RequestAsync<BuildingResult>(request, ct: ct);
        }

        public async Task<BuildingResult> BuyAnimalAsync(Guid buildingId, AnimalKind kind, string name, CancellationToken ct)
        {
            await Task.Delay(400, ct);
            using BuyAnimal request = new() { BuildingId = buildingId, Kind = (byte)kind, Name = name };
            return await _session.RequestAsync<BuildingResult>(request, ct: ct);
        }

        public async Task<BuildingsSnapshot> BuildingsAsync(bool mine, CancellationToken ct)
        {
            await Task.Delay(300, ct);
            using GetBuildings request = new() { Mine = mine };
            return await _session.RequestAsync<BuildingsSnapshot>(request, ct: ct);
        }

        public async Task<AnimalsSnapshot> AnimalsAsync(CancellationToken ct)
        {
            await Task.Delay(300, ct);
            using GetAnimals request = new();
            return await _session.RequestAsync<AnimalsSnapshot>(request, ct: ct);
        }

        public async Task<BuildingResult> FeedAsync(int slot, int quantity, CancellationToken ct)
        {
            await Task.Delay(300, ct);
            using FeedAnimals request = new() { Slot = slot, Quantity = quantity };
            return await _session.RequestAsync<BuildingResult>(request, ct: ct);
        }

        public async Task<AnimalResult> PetAsync(Guid animalId, CancellationToken ct)
        {
            await Task.Delay(300, ct);
            using PetAnimal request = new() { AnimalId = animalId };
            return await _session.RequestAsync<AnimalResult>(request, ct: ct);
        }

        public async Task<AnimalResult> CollectAsync(Guid animalId, int slot, CancellationToken ct)
        {
            await Task.Delay(100, ct);
            using CollectProduce request = new() { AnimalId = animalId, Slot = slot };
            return await _session.RequestAsync<AnimalResult>(request, ct: ct);
        }

        public async Task<PlaceResult> MoveBuildingAsync(int objectId, int x, int y, CancellationToken ct)
        {
            await Task.Delay(400, ct);
            using MoveObject request = new() { ObjectId = objectId, TileX = x, TileY = y, Rotation = 0 };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        public async Task<PlaceResult> PickUpAsync(int objectId, CancellationToken ct)
        {
            await Task.Delay(400, ct);
            using PickUpObject request = new() { ObjectId = objectId };
            return await _session.RequestAsync<PlaceResult>(request, ct: ct);
        }

        // Sets the blueprint down on the first free spot within reach of where the player stands (trees, rocks and weeds the server knows about are skipped by asking again).
        public async Task<(PlaceResult Result, int X, int Y)> PlaceBuildingAsync(TestHost host, int slot, BuildingDefinition definition, CancellationToken ct)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? map);
            int startX = (int)(map!.SpawnX / 16);
            int startY = (int)(map.SpawnY / 16);
            int width = definition.View.FootprintWidth;
            int height = definition.View.FootprintHeight;
            List<(int X, int Y)> candidates = [];
            for (int y = startY - 12; y <= startY + 8; y++)
            {
                for (int x = startX - 16; x <= startX + 10; x++)
                {
                    // The player is stood in front of each spot below, so reach does not limit the search: the farm's random debris can cover every spot near the spawn.
                    bool open = true;
                    for (int dy = 0; dy <= height && open; dy++)
                    {
                        for (int dx = 0; dx < width && open; dx++)
                        {
                            open = !map.Grid.IsBlocked(x + dx, y + dy) && !map.ReservedTiles.Contains((x + dx, y + dy));
                        }
                    }

                    if (open)
                    {
                        candidates.Add((x, y));
                    }
                }
            }

            PlaceResult? last = null;
            foreach ((int x, int y) in candidates.OrderBy(c => Math.Abs(c.X - startX) + Math.Abs(c.Y - startY)))
            {
                // The player stands in front of the spot's door so that it is in reach and not under their feet.
                await host.PutAsync(this.AccountId, ((x + definition.DoorX) * 16) + 8, ((y + height + 1) * 16) + 8, Facing.Up);
                await Task.Delay(400, ct);
                using PlaceObject request = new() { Slot = (byte)slot, TileX = x, TileY = y, Rotation = 0 };
                last = await _session.RequestAsync<PlaceResult>(request, ct: ct);
                if (last.Status == PlaceStatus.Ok)
                {
                    return (last, x, y);
                }
            }

            return (last ?? throw new InvalidOperationException("No spot for the building was within reach."), 0, 0);
        }

        // One step of walking: the real client sends about every 100 ms; the server checks speed against the position it keeps.
        public async Task StepAsync(TestHost host, uint sequence, double x, double y, Facing facing, CancellationToken ct)
        {
            await Task.Delay(250, ct);
            using MoveIntent intent = new() { Sequence = sequence, X = (float)x, Y = (float)y, Facing = (byte)facing, Moving = true };
            await _session.SendAsync(intent, ct);
            await Task.Delay(400, ct);
            _ = host;
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
