// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
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
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Abstractions.Visits;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Visit;
using Nosum.Contracts.World.S2C;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Domain.Nodes;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Nodes;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Visiting another player's farm and farmhouse (docs/adr/ADR-019): the allow-list, the co-op setting, the visit request, what a visitor may and may not
/// touch, and being sent out when the permission goes away. Real Nalix WebSocket clients against the real host and a real PostgreSQL database.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class VisitWireTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero); // after the calendar epoch, so game days pass

    private readonly PostgresFixture _database;

    public VisitWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Visit_ForEveryWayItCanBeRefused_MovesNobodyAndSaysWhy()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        await using VisitClient b = await host.ConnectAsync(bob);
        _ = await b.JoinAsync(host.Timeout);

        (await b.VisitAsync("no!", host.Timeout)).Should().Be(VisitStatus.InvalidNickname);
        (await b.VisitAsync(TestNicknames.Next(), host.Timeout)).Should().Be(VisitStatus.UnknownPlayer);
        (await b.VisitAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.Self);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed, "the owner never invited Bob");
        WhereIs(bob.Id).Should().Be(MapInstance.Shared(GameMaps.Town));

        // A visit starts on a public map: from his own farm, Bob has to walk out first.
        await PutOnAsync(bob, GameMaps.Farm);
        _ = await b.JoinAsync(host.Timeout);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotOnPublicMap);
        WhereIs(bob.Id).Should().Be(new MapInstance(GameMaps.Farm, bob.Id));
    }

    [Fact]
    public async Task Visit_ListedPlayer_EntersTheOwnersFarm_SeesAndIsSeen_AndRemovalSendsThemToTown()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        await using VisitClient a = await host.ConnectAsync(alice);
        await using VisitClient b = await host.ConnectAsync(bob);
        JoinMapResult aliceJoin = await a.JoinAsync(host.Timeout);
        JoinMapResult bobJoin = await b.JoinAsync(host.Timeout);

        // The owner edits the list over the wire; the nickname is matched whatever the case.
        (await a.AddVisitorAsync(bob.Nickname.ToUpperInvariant(), host.Timeout)).Should().Be(VisitStatus.Ok);
        (await a.AddVisitorAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.AlreadyListed);
        VisitorsCopy list = await a.GetVisitorsAsync(host.Timeout);
        list.CoopMembersMayVisit.Should().BeTrue("co-op members may visit by default");
        list.Visitors.Should().ContainSingle().Which.Should().BeEquivalentTo(new { PlayerId = bob.Id, Nickname = bob.Nickname, Listed = true, Present = false });

        // Bob asks to visit: the server moves him into Alice's farm.
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        MapChanged entered = await b.MapChanges.WaitAsync(host.Timeout);
        entered.MapId.Should().Be(GameMaps.Farm);
        entered.PlayerId.Should().Be(bobJoin.PlayerId, "the player id stays the same for the whole connection");
        WhereIs(bob.Id).Should().Be(new MapInstance(GameMaps.Farm, alice.Id));

        // They see each other through the farm's own presence group, and nobody else's farm.
        _ = await a.Snapshots.WaitAsync(s => s.Contains(bobJoin.PlayerId) && s.Contains(aliceJoin.PlayerId), host.Timeout);
        _ = await b.Snapshots.WaitAsync(s => s.Contains(bobJoin.PlayerId) && s.Contains(aliceJoin.PlayerId), host.Timeout);
        (await a.GetVisitorsAsync(host.Timeout)).Visitors.Should().ContainSingle().Which.Present.Should().BeTrue();

        // Revoking sends him to the Town at once, and he cannot come back.
        (await a.RemoveVisitorAsync(bob.Id, host.Timeout)).Should().Be(VisitStatus.Ok);
        (await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Town, host.Timeout)).PlayerId.Should().Be(bobJoin.PlayerId);
        WhereIs(bob.Id).Should().Be(MapInstance.Shared(GameMaps.Town));
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);
        (await a.RemoveVisitorAsync(bob.Id, host.Timeout)).Should().Be(VisitStatus.NotListed);
        (await a.GetVisitorsAsync(host.Timeout)).Visitors.Should().BeEmpty();
    }

    [Fact]
    public async Task Visitor_OnTheOwnersFarm_CanWaterTheCropsButNotTillPlantOrHarvest()
    {
        FakeTimeProvider clock = new(s_start);
        await using TestHost host = await TestHost.StartAsync(_database, clock);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        await using VisitClient a = await host.ConnectAsync(alice);
        await using VisitClient b = await host.ConnectAsync(bob);
        JoinMapResult join = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        (await a.AddVisitorAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
        (int X, int Y)[] tiles = [.. NearbyTiles(join).Where(t => farm!.Tillable!.IsTillable(t.X, t.Y)).Take(3)];

        // Alice tills two tiles and plants one (hotbar: 0 hoe, 1 watering can, 2 parsnip seeds, 8 empty hands).
        (await a.UseAsync(0, tiles[0].X, tiles[0].Y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        (await a.UseAsync(0, tiles[1].X, tiles[1].Y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        (await a.UseAsync(2, tiles[0].X, tiles[0].Y, host.Timeout)).Action.Should().Be(FarmAction.Planted);

        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        FarmSnapshot seen = await b.GetFarmAsync(host.Timeout);
        seen.Tiles.Should().Contain(t => t.X == tiles[0].X && t.Y == tiles[0].Y && t.CropId == CropCatalog.Parsnip, "a visitor sees the owner's crops");

        // Everything but watering is refused, and nothing changes: till bare soil, plant on tilled soil, re-till, harvest with empty hands.
        (await b.UseAsync(0, tiles[2].X, tiles[2].Y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        (await b.UseAsync(2, tiles[1].X, tiles[1].Y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        (await b.UseAsync(0, tiles[1].X, tiles[1].Y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        (await b.UseAsync(8, tiles[0].X, tiles[0].Y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        (await a.GetFarmAsync(host.Timeout)).Tiles.Should().HaveCount(2).And.Contain(t => t.X == tiles[0].X && t.CropId == CropCatalog.Parsnip);

        // Watering is allowed: the crop is wet, and it grows from the visitor's watering alone.
        (await b.UseAsync(1, tiles[0].X, tiles[0].Y, host.Timeout)).Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Watered });
        FarmTileRow wet = await a.Tiles.WaitAsync(t => t.X == tiles[0].X && t.Y == tiles[0].Y && t.WateredDay > 0, host.Timeout);
        wet.CropId.Should().Be(CropCatalog.Parsnip);

        // The visitor keeps watering it on each game day (docs/adr/ADR-021 D7) until it is ripe.
        TimeSpan gameDay = TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs);
        for (int day = 1; day < CropCatalog.All[0].TotalGrowthDays; day++)
        {
            clock.Advance(gameDay);
            await Task.Delay(330, host.Timeout); // the tool swing cooldown runs on real time (ADR-021 D8)
            (await b.UseAsync(1, tiles[0].X, tiles[0].Y, host.Timeout)).Action.Should().Be(FarmAction.Watered);
        }

        clock.Advance(gameDay);
        await Task.Delay(330, host.Timeout);

        // A ripe crop is the owner's: neither empty hands nor the watering can harvest it for a visitor.
        (await b.UseAsync(8, tiles[0].X, tiles[0].Y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        (await b.UseAsync(1, tiles[0].X, tiles[0].Y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        (await a.GetFarmAsync(host.Timeout)).Tiles.Should().Contain(t => t.X == tiles[0].X && t.CropId == CropCatalog.Parsnip);
        (await b.GetInventoryAsync(host.Timeout)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Parsnip, "nothing was harvested into the visitor's bag");
        (await a.UseAsync(8, tiles[0].X, tiles[0].Y, host.Timeout)).Action.Should().Be(FarmAction.Harvested);
    }

    [Fact]
    public async Task Visitor_OnTheOwnersFarm_CannotChopBreakOrCutTheirNodes_AndPaysNoEnergy()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
        (int X, int Y)[] free = [.. Enumerable.Range(-2, 5)
            .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: (int)(farm!.SpawnX / 16) + dx, Y: (int)(farm.SpawnY / 16) + dy)))
            .Where(t => !farm!.Grid.IsBlocked(t.X, t.Y) && !farm.ReservedTiles.Contains(t) && FarmRules.InReach(farm.SpawnX, farm.SpawnY, t.X, t.Y, 16))
            .OrderBy(t => Math.Abs(t.X - (int)(farm!.SpawnX / 16)) + Math.Abs(t.Y - (int)(farm.SpawnY / 16)))
            .Skip(1)
            .Take(3)];

        // Alice's farm holds a grown tree, a stone and a weed; the instance is marked seeded so the map's random first fill never happens.
        await using (NosumDbContext db = new(_database.Options))
        {
            (NodeKind Kind, short Stage)[] kinds = [(NodeKind.Tree, TreeStage.Grown), (NodeKind.Stone, 0), (NodeKind.Weed, 0)];
            for (int i = 0; i < kinds.Length; i++)
            {
                _ = db.WorldNodes.Add(new WorldNode
                {
                    MapId = GameMaps.Farm,
                    OwnerId = alice.Id,
                    TileX = free[i].X,
                    TileY = free[i].Y,
                    Kind = (short)kinds[i].Kind,
                    Stage = kinds[i].Stage,
                    Hits = NodeCatalog.HitsFor(kinds[i].Kind, (byte)kinds[i].Stage),
                    SpawnedAt = s_start,
                });
            }

            _ = db.NodeInstances.Add(new NodeInstanceState { MapId = GameMaps.Farm, OwnerId = alice.Id, SeededAt = s_start, LastDay = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today });
            _ = await db.SaveChangesAsync();
        }

        await using VisitClient a = await host.ConnectAsync(alice);
        await using VisitClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        (await a.AddVisitorAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        MapChanged entered = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        Math.Abs(entered.X - farm!.SpawnX).Should().BeLessThan(1f, "the visitor stands where the nodes are in reach");

        // The axe on the tree, the pickaxe on the stone and the scythe on the weed are all the owner's to use (the swing cooldown is 300 ms).
        int axe = Array.FindIndex(StarterKit.Items, i => i.ItemId == ItemCatalog.Axe);
        int pickaxe = Array.FindIndex(StarterKit.Items, i => i.ItemId == ItemCatalog.Pickaxe);
        int scythe = Array.FindIndex(StarterKit.Items, i => i.ItemId == ItemCatalog.Scythe);
        foreach ((int slot, (int x, int y)) in new[] { (axe, free[0]), (pickaxe, free[1]), (scythe, free[2]) })
        {
            await Task.Delay(350, host.Timeout);
            (await b.UseAsync(slot, x, y, host.Timeout)).Status.Should().Be(FarmStatus.NotYourFarm);
        }

        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.WorldNodes.CountAsync(n => n.OwnerId == alice.Id)).Should().Be(3, "nothing was chopped, broken or cut");
            (await db.WorldNodes.SingleAsync(n => n.Kind == (short)NodeKind.Tree && n.OwnerId == alice.Id)).Hits.Should().Be(NodeCatalog.HitsFor(NodeKind.Tree, TreeStage.Grown));
        }

        (await b.GetInventoryAsync(host.Timeout)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Wood || s.ItemId == ItemCatalog.Stone || s.ItemId == ItemCatalog.Fiber);
        PlayerProgressView progress = await InstanceManager.Instance.GetExistingInstance<IPlayerProgress>()!.GetAsync(bob.Id);
        progress.Should().Be(new PlayerProgressView(EnergyRules.MaxEnergy, EnergyRules.MaxEnergy, 0, 0, 0), "a refused use costs no energy and earns no experience");

        // The owner can: the same weed falls to Alice's scythe.
        await Task.Delay(350, host.Timeout);
        (await a.UseAsync(scythe, free[2].X, free[2].Y, host.Timeout)).Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Broke });
    }

    [Fact]
    public async Task Visitor_InTheOwnersFarmhouse_CanLookButNotPlaceMovePickUpOrShip()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.FarmHouse);
        await PutOnAsync(bob, GameMaps.Town);
        await using VisitClient a = await host.ConnectAsync(alice);
        await using VisitClient b = await host.ConnectAsync(bob);
        JoinMapResult aliceJoin = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        (await a.AddVisitorAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        (int X, int Y)[] free = [.. FreeTilesNear(aliceJoin.SpawnX, aliceJoin.SpawnY).Take(2)];

        // Alice puts a chair in her farmhouse.
        (await a.GrantAsync(ItemCatalog.OakChair, 2, host.Timeout)).Should().BeTrue();
        PlaceResult placed = await a.PlaceAsync(StarterKit.Items.Length, free[0].X, free[0].Y, 0, host.Timeout);
        placed.Status.Should().Be(PlaceStatus.Ok);

        // Bob visits the farm, cannot use the shipping bin there, then walks through the farmhouse door: it leads to Alice's house, not his own.
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        MapChanged onFarm = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        (await b.ShipAsync(0, ItemCatalog.Hoe, 1, host.Timeout)).Status.Should().Be(MarketStatus.NotYourBin);
        await b.SendMoveAsync(1, onFarm.X, (15 * 16) + 15, 2, host.Timeout);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.FarmHouse, host.Timeout);
        WhereIs(bob.Id).Should().Be(new MapInstance(GameMaps.FarmHouse, alice.Id));
        _ = await a.Snapshots.WaitAsync(s => s.Contains(b.MapPlayerId), host.Timeout);

        // He sees the chair, but every change is the owner's.
        (await b.GetPlacedAsync(host.Timeout)).Objects.Should().ContainSingle().Which.Id.Should().Be(placed.ObjectId);
        (await b.GrantAsync(ItemCatalog.OakChair, 2, host.Timeout)).Should().BeTrue();
        (await b.PlaceAsync(StarterKit.Items.Length, free[1].X, free[1].Y, 0, host.Timeout)).Status.Should().Be(PlaceStatus.NotYourPlace);
        (await b.MoveObjectAsync(placed.ObjectId, free[1].X, free[1].Y, 1, host.Timeout)).Status.Should().Be(PlaceStatus.NotYourPlace);
        (await b.PickUpAsync(placed.ObjectId, host.Timeout)).Status.Should().Be(PlaceStatus.NotYourPlace);
        PlacedSnapshot after = await a.GetPlacedAsync(host.Timeout);
        after.Objects.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = placed.ObjectId, TileX = (ushort)free[0].X, TileY = (ushort)free[0].Y, Rotation = (byte)0 });
        (await b.GetInventoryAsync(host.Timeout)).Slots.Should().Contain(s => s.Slot == StarterKit.Items.Length && s.ItemId == ItemCatalog.OakChair && s.Quantity == 2, "the visitor's own chairs stay in his bag");

        // The owner still can, and removing the visitor sends him out of her house too.
        (await a.MoveObjectAsync(placed.ObjectId, free[1].X, free[1].Y, 1, host.Timeout)).Status.Should().Be(PlaceStatus.Ok);
        (await a.RemoveVisitorAsync(bob.Id, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Town, host.Timeout);
        WhereIs(bob.Id).Should().Be(MapInstance.Shared(GameMaps.Town));
    }

    [Fact]
    public async Task Kick_SendsAVisitorOutButKeepsThemOnTheList()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        await using VisitClient a = await host.ConnectAsync(alice);
        await using VisitClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        (await a.AddVisitorAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);

        (await a.KickVisitorAsync(bob.Id, host.Timeout)).Should().Be(VisitStatus.NotListed, "he is not in the place yet");
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        (await a.KickVisitorAsync(bob.Id, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Town, host.Timeout);
        WhereIs(bob.Id).Should().Be(MapInstance.Shared(GameMaps.Town));

        (await a.GetVisitorsAsync(host.Timeout)).Visitors.Should().ContainSingle().Which.Listed.Should().BeTrue();
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok, "kicked, not banned");
    }

    [Fact]
    public async Task CoopMember_MayVisitUntilTheSettingIsTurnedOffOrTheyLeaveTheCoop()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        await using (NosumDbContext context = new(_database.Options))
        {
            Coop coop = new() { Name = "Test coop", CreatedAt = s_start };
            _ = context.Coops.Add(coop);
            _ = context.CoopMembers.Add(new CoopMember { CoopId = coop.Id, PlayerId = alice.Id, Role = CoopRole.Founder, JoinedAt = s_start });
            _ = context.CoopMembers.Add(new CoopMember { CoopId = coop.Id, PlayerId = bob.Id, Role = CoopRole.Member, JoinedAt = s_start });
            _ = await context.SaveChangesAsync();
        }

        await using VisitClient a = await host.ConnectAsync(alice);
        await using VisitClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);

        // Not on the list, but in the same co-op: in. The owner sees him as an unlisted visitor.
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        (await a.GetVisitorsAsync(host.Timeout)).Visitors.Should().ContainSingle().Which.Should().BeEquivalentTo(new { PlayerId = bob.Id, Listed = false, Present = true });

        // Turning the setting off sends him out and keeps him out.
        (await a.SetCoopVisitsAsync(false, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Town, host.Timeout);
        (await a.GetVisitorsAsync(host.Timeout)).CoopMembersMayVisit.Should().BeFalse();
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);

        // Back on: in again; leaving the co-op takes the way in with it, wherever he stands.
        (await a.SetCoopVisitsAsync(true, host.Timeout)).Should().Be(VisitStatus.Ok);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        ICoopService coops = InstanceManager.Instance.GetExistingInstance<ICoopService>()!;
        (await coops.LeaveAsync(bob.Id)).Should().Be(CoopStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Town, host.Timeout);
        WhereIs(bob.Id).Should().Be(MapInstance.Shared(GameMaps.Town));
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);
    }

    [Fact]
    public async Task ListedVisitor_CanVisitAnOfflineOwner_AndTheListSurvivesARestart()
    {
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(bob, GameMaps.Town);
        await using (NosumDbContext context = new(_database.Options))
        {
            _ = context.FarmPlots.Add(new FarmPlot { MapId = GameMaps.Farm, OwnerId = alice.Id, X = 5, Y = 6, CropId = CropCatalog.Parsnip, PlantedAtMs = 1, UpdatedAt = s_start });
            _ = await context.SaveChangesAsync();
        }

        await using (TestHost first = await TestHost.StartAsync(_database))
        {
            IVisitService visits = InstanceManager.Instance.GetExistingInstance<IVisitService>()!;
            (await visits.AddAsync(alice.Id, bob.Nickname)).Should().Be(VisitStatus.Ok);
        }

        // A fresh server, Alice never signs in: the entry is read from PostgreSQL, and the farm is hers as she left it.
        await using TestHost host = await TestHost.StartAsync(_database);
        await using VisitClient b = await host.ConnectAsync(bob);
        _ = await b.JoinAsync(host.Timeout);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        _ = await b.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        (await b.GetFarmAsync(host.Timeout)).Tiles.Should().ContainSingle(t => t.X == 5 && t.Y == 6 && t.CropId == CropCatalog.Parsnip);
        IVisitService service = InstanceManager.Instance.GetExistingInstance<IVisitService>()!;
        VisitorsView view = await service.GetAsync(alice.Id);
        view.Visitors.Should().ContainSingle().Which.Should().BeEquivalentTo(new { PlayerId = bob.Id, Listed = true, Present = true });
    }

    [Fact]
    public async Task Guests_CanVisitWhenInvited_ButKeepNoListAndCannotBeVisited()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        IVisitService visits = InstanceManager.Instance.GetExistingInstance<IVisitService>()!;
        Account alice = await RegisterAsync();
        Account guest = await StartGuestAsync();
        Account other = await RegisterAsync();

        // A guest keeps no allow-list and no co-op setting; the neutral guest name can still be put on somebody else's list.
        (await visits.AddAsync(guest.Id, other.Nickname)).Should().Be(VisitStatus.GuestOwner);
        (await visits.SetCoopMembersMayVisitAsync(guest.Id, false)).Should().Be(VisitStatus.GuestOwner);
        (await visits.GetAsync(guest.Id)).IsGuest.Should().BeTrue();
        (await visits.AddAsync(alice.Id, guest.Nickname)).Should().Be(VisitStatus.Ok);

        // Even a row that should not exist (a guest never makes one) opens nothing: a guest's farm stays private.
        await using (NosumDbContext context = new(_database.Options))
        {
            _ = context.VisitGrants.Add(new VisitGrant { OwnerId = guest.Id, VisitorId = other.Id, CreatedAt = s_start });
            _ = await context.SaveChangesAsync();
        }

        await PutOnAsync(guest, GameMaps.Town);
        await PutOnAsync(other, GameMaps.Town);
        await using VisitClient g = await host.ConnectAsync(guest);
        await using VisitClient o = await host.ConnectAsync(other);
        _ = await g.JoinAsync(host.Timeout);
        _ = await o.JoinAsync(host.Timeout);
        (await g.GetVisitorsAsync(host.Timeout)).IsGuest.Should().BeTrue();
        (await o.VisitAsync(guest.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);
        (await g.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok, "the invited guest may visit");
        _ = await g.MapChanges.WaitAsync(m => m.MapId == GameMaps.Farm, host.Timeout);
        WhereIs(guest.Id).Should().Be(new MapInstance(GameMaps.Farm, alice.Id));
    }

    [Fact]
    public async Task AllowList_Edits_FollowTheRulesOfNicknamesDuplicatesAndSize()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        IVisitService visits = InstanceManager.Instance.GetExistingInstance<IVisitService>()!;
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();

        (await visits.AddAsync(alice.Id, "no!")).Should().Be(VisitStatus.InvalidNickname);
        (await visits.AddAsync(alice.Id, TestNicknames.Next())).Should().Be(VisitStatus.UnknownPlayer);
        (await visits.AddAsync(alice.Id, alice.Nickname)).Should().Be(VisitStatus.Self);
        (await visits.RemoveAsync(alice.Id, bob.Id)).Should().Be(VisitStatus.NotListed);
        (await visits.KickAsync(alice.Id, bob.Id)).Should().Be(VisitStatus.NotListed);
        (await visits.KickAsync(alice.Id, alice.Id)).Should().Be(VisitStatus.Self);
        (await visits.AddAsync(alice.Id, bob.Nickname)).Should().Be(VisitStatus.Ok);
        (await visits.AddAsync(alice.Id, bob.Nickname)).Should().Be(VisitStatus.AlreadyListed);

        // The list holds 50: fill it with hand-built players, then the next one is refused.
        await using (NosumDbContext context = new(_database.Options))
        {
            for (int i = 1; i < Nosum.Gameplay.Visits.VisitRules.MaxListed; i++)
            {
                Player filler = TestNicknames.Name(new Player { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start, Language = GameLanguage.English });
                _ = context.Players.Add(filler);
                _ = context.VisitGrants.Add(new VisitGrant { OwnerId = alice.Id, VisitorId = filler.Id, CreatedAt = s_start });
            }

            _ = await context.SaveChangesAsync();
        }

        Account late = await RegisterAsync();
        (await visits.AddAsync(alice.Id, late.Nickname)).Should().Be(VisitStatus.ListFull);
        (await visits.RemoveAsync(alice.Id, bob.Id)).Should().Be(VisitStatus.Ok);
        (await visits.AddAsync(alice.Id, late.Nickname)).Should().Be(VisitStatus.Ok);
    }

    private static MapInstance? WhereIs(Guid accountId)
    {
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        List<string> keys = [];
        presence.CopyActiveMapIds(keys);
        foreach (string key in keys)
        {
            List<PlayerPresence> here = [];
            presence.CopyPlayers(key, here);
            PlayerPresence? found = here.FirstOrDefault(p => p.AccountId == accountId);
            if (found is not null)
            {
                return found.Instance;
            }
        }

        return null;
    }

    // Registers an account through the service (sign-ups over the wire are rate limited per address and every test shares one).
    private static async Task<Account> RegisterAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        string email = "v" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        AccountOutcome outcome = await accounts.RegisterAsync(email, "password-1", TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, outcome.SessionToken, outcome.Nickname);
    }

    private static async Task<Account> StartGuestAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        AccountOutcome outcome = await accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, outcome.SessionToken, outcome.Nickname);
    }

    // Saves where the player was last, so the server's join puts them there (a bad place falls back to that map's spawn).
    private static async Task PutOnAsync(Account account, string mapId)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(account.Id, new PlayerStateRow(mapId, -1, -1, Facing.Down), CancellationToken.None);
    }

    // Tiles whose centre is within reach of the spawn feet, nearest first.
    private static IEnumerable<(int X, int Y)> NearbyTiles(JoinMapResult join)
    {
        int tx = (int)(join.SpawnX / 16);
        int ty = (int)(join.SpawnY / 16);
        return Enumerable.Range(-2, 5)
            .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
            .Where(t => FarmRules.InReach(join.SpawnX, join.SpawnY, t.X, t.Y, 16))
            .OrderBy(t => Math.Abs(((t.X + 0.5) * 16) - join.SpawnX) + Math.Abs(((t.Y + 0.5) * 16) - join.SpawnY));
    }

    // Walkable, unreserved farmhouse tiles near a spawn that the player can reach, nearest first.
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
                && FarmRules.InReach(feetX, feetY, t.X, t.Y, 16))
            .OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty));
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed record Account(Guid Id, string Token, string Nickname);

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(120));

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

            // Development, so the tester grant can hand out furniture.
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public Task<VisitClient> ConnectAsync(Account account) => VisitClient.ConnectAsync(this.Port, account.Token, this.Timeout);

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
