// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Furnaces (docs/adr/ADR-024 D4) against a real PostgreSQL database with a clock the test moves: five ore and one coal go in together with the job, the bar is
/// ready after thirty game minutes, collecting needs room, a furnace with a job cannot be picked up, and only the owner (or the co-op) uses it.
/// </summary>
public sealed class FurnaceServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_smelting = TimeSpan.FromMilliseconds(FurnaceRules.ReadyAtMs(0, GameCalendar.Default));

    private readonly PostgresFixture _database;

    public FurnaceServiceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Start_TakesOreAndCoalWithTheJob_AndTellsTheInstance()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 12);
        await world.GiveAsync(owner, ItemCatalog.Coal, 3);

        FurnaceOutcome outcome = await world.Furnaces.StartAsync(world.Presence.ConnectionOf(owner), furnace, ItemCatalog.CopperOre);

        outcome.Should().Be(new FurnaceOutcome(FurnaceStatus.Ok, furnace, 0, 0));
        (await world.CountAsync(owner, ItemCatalog.CopperOre)).Should().Be(7);
        (await world.CountAsync(owner, ItemCatalog.Coal)).Should().Be(2);
        await using NosumDbContext db = world.Db();
        FurnaceJob job = await db.FurnaceJobs.SingleAsync(j => j.ObjectId == furnace);
        (job.OreItemId, job.StartedAtMs).Should().Be((ItemCatalog.CopperOre, PlacementHarness.Start.ToUnixTimeMilliseconds()));
        FurnaceChanged push = world.Presence.Sent<FurnaceChanged>().Single();
        (push.ObjectId, push.State, push.OreItemId, push.RemainingMs).Should().Be((furnace, FurnaceStateCode.Working, ItemCatalog.CopperOre, (int)s_smelting.TotalMilliseconds));
        List<Nosum.Domain.Inventory.InventoryLogEntry> smelted = await db.InventoryLog.Where(e => e.PlayerId == owner && e.Reason == "furnace.smelt").ToListAsync();
        smelted.Select(e => (e.ItemId, e.Delta)).Should().BeEquivalentTo([(ItemCatalog.CopperOre, -5), (ItemCatalog.Coal, -1)]);
    }

    [Fact]
    public async Task Start_Refusals_ChangeNothing()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 4);
        await world.GiveAsync(owner, ItemCatalog.Coal, 1);
        await world.GiveAsync(owner, ItemCatalog.Stone, 9);

        (await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.NotEnough, "four ore are too few");
        (await world.Furnaces.StartAsync(me, furnace, ItemCatalog.Stone)).Status.Should().Be(FurnaceStatus.NotSmeltable);
        (await world.Furnaces.StartAsync(me, 987_654, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.UnknownObject);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 1);
        await world.Inventory.RemoveAsync(owner, ItemCatalog.Coal, 1, "test.setup");
        (await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.NotEnough, "no coal");

        (await world.CountAsync(owner, ItemCatalog.CopperOre)).Should().Be(5);
        await using NosumDbContext db = world.Db();
        (await db.FurnaceJobs.AnyAsync(j => j.ObjectId == furnace)).Should().BeFalse();
    }

    [Fact]
    public async Task Start_WhileBusy_IsRefusedWithoutTakingMoreOre()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 10);
        await world.GiveAsync(owner, ItemCatalog.Coal, 2);
        (await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.Ok);

        (await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.Busy);

        (await world.CountAsync(owner, ItemCatalog.CopperOre)).Should().Be(5);
        (await world.CountAsync(owner, ItemCatalog.Coal)).Should().Be(1);
    }

    [Fact]
    public async Task Start_TwoRequestsAtOnce_StartExactlyOneJob()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 50);
        await world.GiveAsync(owner, ItemCatalog.Coal, 5);

        FurnaceOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre))));

        outcomes.Count(o => o.Status == FurnaceStatus.Ok).Should().Be(1);
        outcomes.Where(o => o.Status != FurnaceStatus.Ok).Should().OnlyContain(o => o.Status == FurnaceStatus.Busy);
        (await world.CountAsync(owner, ItemCatalog.CopperOre)).Should().Be(45);
        (await world.CountAsync(owner, ItemCatalog.Coal)).Should().Be(4);
    }

    [Fact]
    public async Task Collect_IsRefusedUntilThirtyGameMinutesPassed_ThenGivesOneBarAndFreesTheFurnace()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.IronOre, 5);
        await world.GiveAsync(owner, ItemCatalog.Coal, 1);
        (await world.Furnaces.CollectAsync(me, furnace)).Status.Should().Be(FurnaceStatus.NothingToCollect);
        (await world.Furnaces.StartAsync(me, furnace, ItemCatalog.IronOre)).Status.Should().Be(FurnaceStatus.Ok);

        world.Clock.Advance(s_smelting - TimeSpan.FromSeconds(1));
        (await world.Furnaces.CollectAsync(me, furnace)).Status.Should().Be(FurnaceStatus.NotReady);
        (await world.Furnaces.GetAsync(me)).Should().ContainSingle().Which.Should().Be(new FurnaceView(furnace, FurnaceStateCode.Working, ItemCatalog.IronOre, 1000));

        world.Clock.Advance(TimeSpan.FromSeconds(1));
        (await world.Furnaces.GetAsync(me)).Should().ContainSingle().Which.State.Should().Be(FurnaceStateCode.Ready);
        FurnaceOutcome collected = await world.Furnaces.CollectAsync(me, furnace);

        collected.Should().Be(new FurnaceOutcome(FurnaceStatus.Ok, furnace, ItemCatalog.IronBar, 1));
        (await world.CountAsync(owner, ItemCatalog.IronBar)).Should().Be(1);
        (await world.Furnaces.CollectAsync(me, furnace)).Status.Should().Be(FurnaceStatus.NothingToCollect, "a bar is collected once");
        (await world.Furnaces.GetAsync(me)).Should().BeEmpty();
        world.Presence.Sent<FurnaceChanged>()[^1].State.Should().Be(FurnaceStateCode.Idle);
        await using NosumDbContext db = world.Db();
        (await db.FurnaceJobs.AnyAsync(j => j.ObjectId == furnace)).Should().BeFalse("the job row is gone");
    }

    [Fact]
    public async Task Collect_TwoRequestsAtOnce_PayOneBar()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 5);
        await world.GiveAsync(owner, ItemCatalog.Coal, 1);
        _ = await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre);
        world.Clock.Advance(s_smelting);

        FurnaceOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => world.Furnaces.CollectAsync(me, furnace))));

        outcomes.Count(o => o.Status == FurnaceStatus.Ok).Should().Be(1);
        (await world.CountAsync(owner, ItemCatalog.CopperBar)).Should().Be(1);
    }

    [Fact]
    public async Task Collect_WithAFullBag_KeepsTheBarInTheFurnace()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 5);
        await world.GiveAsync(owner, ItemCatalog.Coal, 1);
        _ = await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre);
        world.Clock.Advance(s_smelting);
        while ((await world.Inventory.GetAsync(owner)).Slots.Count < 36)
        {
            await world.GiveAsync(owner, ItemCatalog.Fiber, 99);
        }

        (await world.Furnaces.CollectAsync(me, furnace)).Status.Should().Be(FurnaceStatus.NoSpace);

        (await world.Furnaces.GetAsync(me)).Should().ContainSingle().Which.State.Should().Be(FurnaceStateCode.Ready, "the bar waits");
        await world.Inventory.RemoveAsync(owner, ItemCatalog.Fiber, 99, "test.setup");
        (await world.Furnaces.CollectAsync(me, furnace)).Status.Should().Be(FurnaceStatus.Ok);
    }

    [Fact]
    public async Task PickUp_AFurnaceWithAJob_IsRefused_UntilTheBarIsCollected()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, _, _) = await FarmFurnaceAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 5);
        await world.GiveAsync(owner, ItemCatalog.Coal, 1);
        _ = await world.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre);

        (await world.Placeables.PickUpAsync(me, furnace)).Should().Be(new PlaceOutcome(PlaceStatus.InUse, 0));
        world.Clock.Advance(s_smelting);
        (await world.Placeables.PickUpAsync(me, furnace)).Status.Should().Be(PlaceStatus.InUse, "a finished bar inside also keeps the furnace");
        _ = await world.Furnaces.CollectAsync(me, furnace);

        (await world.Placeables.PickUpAsync(me, furnace)).Should().Be(new PlaceOutcome(PlaceStatus.Ok, furnace));
        (await world.CountAsync(owner, ItemCatalog.Furnace)).Should().Be(1);
    }

    [Fact]
    public async Task Furnace_KeepsItsJobWhenMovedAndAcrossARestartOfTheServices()
    {
        Guid owner;
        int furnace;
        int x;
        int y;
        using (PlacementHarness first = new(_database))
        {
            (owner, furnace, x, y) = await FarmFurnaceAsync(first);
            await first.GiveAsync(owner, ItemCatalog.CopperOre, 5);
            await first.GiveAsync(owner, ItemCatalog.Coal, 1);
            Nalix.Abstractions.Networking.IConnection me = first.Presence.ConnectionOf(owner);
            _ = await first.Furnaces.StartAsync(me, furnace, ItemCatalog.CopperOre);
            (await first.Placeables.MoveAsync(me, furnace, x + 2, y, 0)).Status.Should().Be(PlaceStatus.Ok, "a working furnace can be shoved aside");
        }

        using PlacementHarness second = new(_database);
        second.StandOnFarm(owner, x + 2, y + 1);
        second.Clock.Advance(s_smelting);

        FurnaceOutcome collected = await second.Furnaces.CollectAsync(second.Presence.ConnectionOf(owner), furnace);

        collected.Should().Be(new FurnaceOutcome(FurnaceStatus.Ok, furnace, ItemCatalog.CopperBar, 1));
    }

    [Fact]
    public async Task Start_AVisitor_IsRefused_AndSoIsAnythingBeyondReach()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int furnace, int x, int y) = await FarmFurnaceAsync(world);
        Guid visitor = await world.CreatePlayerAsync();
        world.Stand(visitor, new MapInstance(GameMaps.Farm, owner), x + 1, y);
        await world.GiveAsync(visitor, ItemCatalog.CopperOre, 5);
        await world.GiveAsync(visitor, ItemCatalog.Coal, 1);

        (await world.Furnaces.StartAsync(world.Presence.ConnectionOf(visitor), furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.NotYours);
        (await world.Furnaces.CollectAsync(world.Presence.ConnectionOf(visitor), furnace)).Status.Should().Be(FurnaceStatus.NotYours);
        world.StandOnFarm(owner, x + 1, y + 6);
        await world.GiveAsync(owner, ItemCatalog.CopperOre, 5);
        (await world.Furnaces.StartAsync(world.Presence.ConnectionOf(owner), furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.OutOfReach);
        (await world.CountAsync(visitor, ItemCatalog.CopperOre)).Should().Be(5);
    }

    [Fact]
    public async Task CoopLand_AnyMemberUsesTheFurnace_AndEachStepIsAudited()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid member = await world.CreatePlayerAsync();
        Guid stranger = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, member);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        (int x, int y, _) = world.OpenRow(GameMaps.CoopFarm, 4);
        foreach (Guid who in new[] { founder, member, stranger })
        {
            world.Stand(who, land, x + 1, y);
        }

        int furnace = await world.PlaceOnFarmAsync(founder, ItemCatalog.Furnace, x, y);
        await world.GiveAsync(member, ItemCatalog.CopperOre, 5);
        await world.GiveAsync(member, ItemCatalog.Coal, 1);
        await world.GiveAsync(stranger, ItemCatalog.CopperOre, 5);
        await world.GiveAsync(stranger, ItemCatalog.Coal, 1);

        (await world.Furnaces.StartAsync(world.Presence.ConnectionOf(stranger), furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.NotYours);
        (await world.Furnaces.StartAsync(world.Presence.ConnectionOf(member), furnace, ItemCatalog.CopperOre)).Status.Should().Be(FurnaceStatus.Ok);
        world.Clock.Advance(s_smelting);
        (await world.Furnaces.CollectAsync(world.Presence.ConnectionOf(founder), furnace)).Should().Be(new FurnaceOutcome(FurnaceStatus.Ok, furnace, ItemCatalog.CopperBar, 1));

        await using NosumDbContext db = world.Db();
        List<(string Reason, int ItemId, int Delta)> audit = (await db.CoopLog.Where(e => e.CoopId == coop && e.Reason != CoopLand.PlaceReason).ToListAsync()).Select(e => (e.Reason, e.ItemId, e.Delta)).ToList();
        audit.Should().BeEquivalentTo([(CoopLand.SmeltReason, ItemCatalog.CopperOre, -5), (CoopLand.SmeltReason, ItemCatalog.Coal, -1), (CoopLand.CollectReason, ItemCatalog.CopperBar, 1)]);
    }

    // The owner on their own farm with a furnace placed next to them.
    private static async Task<(Guid Owner, int Furnace, int X, int Y)> FarmFurnaceAsync(PlacementHarness world)
    {
        Guid owner = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 4);
        world.StandOnFarm(owner, x + 1, y);
        int furnace = await world.PlaceOnFarmAsync(owner, ItemCatalog.Furnace, x, y);
        return (owner, furnace, x, y);
    }
}
