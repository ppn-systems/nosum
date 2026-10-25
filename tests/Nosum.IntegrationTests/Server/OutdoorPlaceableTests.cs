// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Farm;
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Quests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Placing the crafted objects on the farm and on co-op land (docs/adr/ADR-024 D3) against a real PostgreSQL database: the rules of where a piece may stand, who may
/// place, the audit of co-op land, the walk grid, the sprinkler index, and the two-way refusal between placed pieces and tilled soil.
/// </summary>
public sealed class OutdoorPlaceableTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public OutdoorPlaceableTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Place_ChestOnTheFarm_IsSavedPublishedAndTakenFromTheBag()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 3);
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.Chest, 1);

        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);

        outcome.Status.Should().Be(PlaceStatus.Ok);
        (await world.CountAsync(player, ItemCatalog.Chest)).Should().Be(0);
        await using Nosum.Infrastructure.Persistence.NosumDbContext db = world.Db();
        PlacedObject row = await db.PlacedObjects.SingleAsync(p => p.Id == outcome.ObjectId);
        (row.MapId, row.OwnerId, row.ItemId, row.TileX, row.TileY).Should().Be((GameMaps.Farm, player, ItemCatalog.Chest, x, y));
        world.Tiles.IsCovered(new MapInstance(GameMaps.Farm, player), x, y).Should().BeTrue();
        PlacedChanged push = world.Presence.Sent<PlacedChanged>().Single(p => p.Upserted.Any(u => u.Id == outcome.ObjectId));
        push.MapId.Should().Be(GameMaps.Farm);
        PlacedSnapshotView view = await world.Placeables.GetAsync(world.Presence.ConnectionOf(player));
        view.Objects.Should().ContainSingle().Which.Should().Be(new PlacedItem(outcome.ObjectId, ItemCatalog.Chest, x, y, 0));
    }

    [Fact]
    public async Task Place_OnTilledSoil_IsRefused_AndTheItemStaysInTheBag()
    {
        using PlacementHarness probe = new(_database);
        (int x, int y, _) = probe.OpenRow(GameMaps.Farm, 4);
        using PlacementHarness world = new(_database, farm: new PlacementHarness.TilledAt((x, y)));
        Guid player = await world.CreatePlayerAsync();
        world.StandOnFarm(player, x + 3, y);
        int slot = await world.GiveAsync(player, ItemCatalog.WoodFence, 1);

        PlaceOutcome outcome = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0);

        outcome.Should().Be(new PlaceOutcome(PlaceStatus.Tilled, 0));
        (await world.CountAsync(player, ItemCatalog.WoodFence)).Should().Be(1);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x + 1, y, 0)).Status.Should().Be(PlaceStatus.Ok, "the soil next to it is free");
    }

    [Fact]
    public async Task Place_OnANode_IsRefused()
    {
        using PlacementHarness probe = new(_database);
        (int x, int y, _) = probe.OpenRow(GameMaps.Farm, 4);
        using PlacementHarness world = new(_database, nodes: new PlacementHarness.NodesAt((x, y)));
        Guid player = await world.CreatePlayerAsync();
        world.StandOnFarm(player, x + 2, y);
        int slot = await world.GiveAsync(player, ItemCatalog.Chest, 1);

        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0)).Status.Should().Be(PlaceStatus.NodeInTheWay);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x + 1, y, 0)).Status.Should().Be(PlaceStatus.Ok);
    }

    [Fact]
    public async Task Place_RefusalsAreReportedWithTheirOwnCodes()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        int slot = await world.GiveAsync(player, ItemCatalog.Chest, 4);

        // Against a wall, on a tile the map keeps clear, and off the map.
        ((int X, int Y) openNextToWall, (int X, int Y) wall) = world.WallSpot(GameMaps.Farm);
        world.StandOnFarm(player, openNextToWall.X, openNextToWall.Y);
        (await PlaceStatusAsync(world, player, slot, wall.X, wall.Y)).Should().Be(PlaceStatus.Blocked);
        (await PlaceStatusAsync(world, player, slot, -1, 0)).Should().Be(PlaceStatus.OutsideRoom);
        ((int X, int Y) openNextToReserved, (int X, int Y) reserved) = world.ReservedSpot(GameMaps.Farm);
        world.StandOnFarm(player, openNextToReserved.X, openNextToReserved.Y);
        (await PlaceStatusAsync(world, player, slot, reserved.X, reserved.Y)).Should().Be(PlaceStatus.Reserved);

        // On open ground: placed once, then occupied, under the player's feet, and too far away.
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 5);
        world.StandOnFarm(player, x + 2, y);
        (await PlaceStatusAsync(world, player, slot, x, y)).Should().Be(PlaceStatus.Ok);
        (await PlaceStatusAsync(world, player, slot, x, y)).Should().Be(PlaceStatus.Overlaps);
        (await PlaceStatusAsync(world, player, slot, x + 2, y)).Should().Be(PlaceStatus.PlayerInTheWay);
        (await PlaceStatusAsync(world, player, slot, x + 2, y + 6)).Should().Be(PlaceStatus.OutOfReach);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), 35, x + 1, y, 0)).Status.Should().Be(PlaceStatus.NoItem, "an empty slot holds nothing to place");
        (await world.CountAsync(player, ItemCatalog.Chest)).Should().Be(3, "only the one successful placing took a chest");
    }

    [Fact]
    public async Task Place_AVisitorInSomeoneElsesFarm_AndAPlayerOnAPublicMap_AreRefused()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid visitor = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 3);
        world.Stand(visitor, new MapInstance(GameMaps.Farm, owner), x + 2, y);
        int slot = await world.GiveAsync(visitor, ItemCatalog.Chest, 1);

        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(visitor), slot, x, y, 0)).Status.Should().Be(PlaceStatus.NotYourPlace);

        world.Stand(visitor, MapInstance.Shared(GameMaps.Town), 20, 20);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(visitor), slot, 20, 21, 0)).Status.Should().Be(PlaceStatus.NotPlaceable);
        (await world.CountAsync(visitor, ItemCatalog.Chest)).Should().Be(1);
    }

    [Fact]
    public async Task Fences_Block_Paths_DoNot_InTheGridPlayersWalkOn()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 5);
        world.StandOnFarm(player, x + 1, y + 1);
        MapInstance instance = new(GameMaps.Farm, player);
        MapDefinition farm = world.Map(GameMaps.Farm);
        int fence = await world.PlaceOnFarmAsync(player, ItemCatalog.WoodFence, x, y);
        int path = await world.PlaceOnFarmAsync(player, ItemCatalog.StonePath, x + 1, y);

        WalkGrid grid = world.Placeables.GridFor(instance, farm);

        grid.IsBlocked(x, y).Should().BeTrue();
        grid.IsBlocked(x + 1, y).Should().BeFalse("a path is walked over");
        farm.Grid.IsBlocked(x, y).Should().BeFalse("the map's own grid is shared and never changed");
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), await world.GiveAsync(player, ItemCatalog.WoodFence, 1), x + 1, y, 0)).Status.Should().Be(PlaceStatus.Overlaps, "a path still takes its tile");

        _ = await world.Placeables.PickUpAsync(world.Presence.ConnectionOf(player), fence);
        world.Placeables.GridFor(instance, farm).IsBlocked(x, y).Should().BeFalse("the tile is free again");
        path.Should().BePositive();
    }

    [Fact]
    public async Task Sprinkler_IsWateringItsFourNeighbours_UntilPickedUp()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 4);
        world.StandOnFarm(player, x + 2, y);
        MapInstance instance = new(GameMaps.Farm, player);

        int sprinkler = await world.PlaceOnFarmAsync(player, ItemCatalog.Sprinkler, x, y);

        world.Tiles.IsSprinklerWatered(instance, x + 1, y).Should().BeTrue();
        world.Tiles.IsSprinklerWatered(instance, x, y + 1).Should().BeTrue();
        world.Tiles.IsSprinklerWatered(instance, x, y).Should().BeFalse();
        world.Tiles.IsSprinklerWatered(instance, x + 1, y + 1).Should().BeFalse();
        world.Tiles.IsSprinklerWatered(new MapInstance(GameMaps.Farm, Guid.NewGuid()), x + 1, y).Should().BeFalse("another farm is another instance");

        _ = await world.Placeables.PickUpAsync(world.Presence.ConnectionOf(player), sprinkler);

        world.Tiles.IsSprinklerWatered(instance, x + 1, y).Should().BeFalse();
        (await world.CountAsync(player, ItemCatalog.Sprinkler)).Should().Be(1);
    }

    [Fact]
    public async Task Move_APiece_ChecksTheSameRules_AndKeepsItsId()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 5);
        world.StandOnFarm(player, x + 1, y + 1);
        int a = await world.PlaceOnFarmAsync(player, ItemCatalog.Chest, x, y);
        int b = await world.PlaceOnFarmAsync(player, ItemCatalog.Chest, x + 1, y);

        (await world.Placeables.MoveAsync(world.Presence.ConnectionOf(player), a, x + 1, y, 0)).Status.Should().Be(PlaceStatus.Overlaps);
        (await world.Placeables.MoveAsync(world.Presence.ConnectionOf(player), a, x + 2, y, 0)).Should().Be(new PlaceOutcome(PlaceStatus.Ok, a));
        (await world.Placeables.MoveAsync(world.Presence.ConnectionOf(player), 987_654, x + 2, y, 0)).Status.Should().Be(PlaceStatus.UnknownObject);

        PlacedSnapshotView view = await world.Placeables.GetAsync(world.Presence.ConnectionOf(player));
        view.Objects.Should().BeEquivalentTo([new PlacedItem(a, ItemCatalog.Chest, x + 2, y, 0), new PlacedItem(b, ItemCatalog.Chest, x + 1, y, 0)]);
    }

    [Fact]
    public async Task CoopLand_MembersPlace_AStrangerCannot_AndEveryChangeIsAudited()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid member = await world.CreatePlayerAsync();
        Guid stranger = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, member);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        (int x, int y, _) = world.OpenRow(GameMaps.CoopFarm, 5);
        foreach (Guid who in new[] { founder, member, stranger })
        {
            world.Stand(who, land, x + 2, y);
        }

        int slot = await world.GiveAsync(stranger, ItemCatalog.Chest, 1);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(stranger), slot, x, y, 0)).Status.Should().Be(PlaceStatus.NotYourPlace, "the land is for the co-op's members");

        int chest = await world.PlaceOnFarmAsync(founder, ItemCatalog.Chest, x, y);
        (await world.Placeables.MoveAsync(world.Presence.ConnectionOf(member), chest, x + 1, y, 0)).Status.Should().Be(PlaceStatus.Ok, "another member may rearrange the land");
        (await world.Placeables.PickUpAsync(world.Presence.ConnectionOf(member), chest)).Status.Should().Be(PlaceStatus.Ok);

        await using Nosum.Infrastructure.Persistence.NosumDbContext db = world.Db();
        List<CoopLogEntry> audit = await db.CoopLog.Where(e => e.CoopId == coop).OrderBy(e => e.OccurredAt).ToListAsync();
        Guid founderMember = (await db.CoopMembers.SingleAsync(m => m.PlayerId == founder)).Id;
        Guid memberMember = (await db.CoopMembers.SingleAsync(m => m.PlayerId == member)).Id;
        audit.Select(e => (e.Reason, e.MemberId, e.ItemId, e.Delta)).Should().BeEquivalentTo(
        [
            (CoopLand.PlaceReason, founderMember, ItemCatalog.Chest, -1),
            (CoopLand.PickUpReason, memberMember, ItemCatalog.Chest, 1),
        ]);
        (await db.PlacedObjects.AnyAsync(p => p.OwnerId == founder || p.OwnerId == member)).Should().BeFalse("nothing was written to anyone's private farm");
        (await db.PlacedObjects.CountAsync(p => p.OwnerId == coop)).Should().Be(0, "the chest was picked up");
    }

    [Fact]
    public async Task CoopLand_ARemovedMember_CannotPlaceAtOnce()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid leaver = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, leaver);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        (int x, int y, _) = world.OpenRow(GameMaps.CoopFarm, 4);
        world.Stand(leaver, land, x + 2, y);
        int slot = await world.GiveAsync(leaver, ItemCatalog.WoodFence, 2);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(leaver), slot, x, y, 0)).Status.Should().Be(PlaceStatus.Ok);

        await using (Nosum.Infrastructure.Persistence.NosumDbContext db = world.Db())
        {
            db.CoopMembers.RemoveRange(await db.CoopMembers.Where(m => m.PlayerId == leaver).ToListAsync());
            _ = await db.SaveChangesAsync();
        }

        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(leaver), slot, x + 1, y, 0)).Status.Should().Be(PlaceStatus.NotYourPlace);
        (await world.CountAsync(leaver, ItemCatalog.WoodFence)).Should().Be(1);
    }

    [Fact]
    public async Task RealFarmLayer_NothingIsPlacedOnTilledSoil_AndNothingIsTilledUnderAPiece()
    {
        FarmService? farm = null;
        using PlacementHarness world = new(_database, farmFactory: h =>
        {
            GroundItemService groundItems = new(h.Presence, NoChunks.Instance, NoChunks.Instance, Maps, new TransferService(h.Inventory, h.Clock), h.Units, h.Clock);
            farm = new FarmService(h.Presence, NoChunks.Instance, NoChunks.Instance, Maps, h.Inventory, new QuestService(h.Units, h.Clock), h.Coops, new NoNodes(), new NoCooldownTools(), h.Units, h.Clock, GameCalendar.Default, groundItems, new PlayerProgressService(h.Units, new CalendarGameDayClock(GameCalendar.Default, h.Clock), h.Inventory, h.Clock), placed: h.Tiles);
            return farm;
        });
        Guid player = await world.CreatePlayerAsync();
        (int x, int y) = world.TillableSpot(GameMaps.Farm);
        world.StandOnFarm(player, x + 1, y);
        int hoe = (await world.Inventory.GetAsync(player)).Slots.First(s => s.ItemId == ItemCatalog.Hoe).Slot;
        Task<FarmUseOutcome> Till(int tx, int ty) => farm!.UseAsync(world.Presence.ConnectionOf(player), hoe, tx, ty);

        // A fence on a tillable tile keeps the hoe off it.
        int fence = await world.PlaceOnFarmAsync(player, ItemCatalog.WoodFence, x, y);
        (await Till(x, y)).Should().Be(new FarmUseOutcome(FarmStatus.Blocked, FarmAction.None));
        _ = await world.Placeables.PickUpAsync(world.Presence.ConnectionOf(player), fence);

        // Once the soil is tilled, nothing may be placed on it.
        (await Till(x, y)).Should().Be(new FarmUseOutcome(FarmStatus.Ok, FarmAction.Tilled));
        int slot = await world.GiveAsync(player, ItemCatalog.WoodFence, 1);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0)).Status.Should().Be(PlaceStatus.Tilled);
        (await world.CountAsync(player, ItemCatalog.WoodFence)).Should().Be(2, "the refused piece stays in the bag next to the one picked up");
    }

    private static readonly TmxMapCatalog Maps = new(GameMaps.All, System.IO.Path.Combine(AppContext.BaseDirectory, "maps"));

    private static async Task<PlaceStatus> PlaceStatusAsync(PlacementHarness world, Guid player, int slot, int x, int y)
        => (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(player), slot, x, y, 0)).Status;
}
