// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Inventory;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Chests (docs/adr/ADR-024 D4) against a real PostgreSQL database: who may open one, atomic audited moves in both directions, swaps and merges, the refusals,
/// two players racing for one stack, a pick-up racing a deposit, and a chest that only goes with its contents out of it.
/// </summary>
public sealed class ChestServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public ChestServiceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Open_TheOwner_SeesAnEmptyThirtySixSlotChest_WhoseInventoryIsCreatedOnFirstUse()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);

        ChestOpenOutcome opened = await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest);

        opened.Status.Should().Be(ChestStatus.Ok);
        (opened.ObjectId, opened.Capacity).Should().Be((chest, 36));
        opened.Slots.Should().BeEmpty();
        await using NosumDbContext db = world.Db();
        PlayerInventory header = await db.PlayerInventories.SingleAsync(i => i.PlayerId == ContainerIds.ForChest(chest));
        header.Capacity.Should().Be(36);
        (await db.InventorySlots.AnyAsync(s => s.PlayerId == ContainerIds.ForChest(chest))).Should().BeFalse("no starter kit goes into a container");
        (await db.InventoryLog.AnyAsync(e => e.PlayerId == ContainerIds.ForChest(chest))).Should().BeFalse();
    }

    [Fact]
    public async Task Open_AVisitor_AnotherPlace_AFarChest_OrNotAChest_IsRefused()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, int x, int y) = await FarmChestAsync(world);
        Guid visitor = await world.CreatePlayerAsync();
        world.Stand(visitor, new MapInstance(GameMaps.Farm, owner), x + 1, y);
        int furnace = await world.PlaceOnFarmAsync(owner, ItemCatalog.Furnace, x, y + 1);

        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(visitor), chest)).Status.Should().Be(ChestStatus.NotYours, "a visitor cannot open the owner's chest");

        world.StandOnFarm(owner, x + 1, y + 6);
        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest)).Status.Should().Be(ChestStatus.OutOfReach);
        world.StandOnFarm(owner, x + 1, y);
        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), 987_654)).Status.Should().Be(ChestStatus.UnknownObject);
        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), furnace)).Status.Should().Be(ChestStatus.UnknownObject, "a furnace is not a chest");

        world.Stand(owner, MapInstance.Shared(GameMaps.Town), 20, 20);
        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest)).Status.Should().Be(ChestStatus.UnknownObject, "chests only exist where objects can be placed");
    }

    [Fact]
    public async Task Move_Deposit_TakesTheStackFromTheBag_AuditsItAndTellsTheViewers()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int slot = await world.GiveAsync(owner, ItemCatalog.Wood, 60);
        _ = await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest);

        ChestStatus status = await world.Chests.MoveAsync(world.Presence.ConnectionOf(owner), chest, ChestSide.Player, slot, ChestSide.Chest, 3, 0);

        status.Should().Be(ChestStatus.Ok);
        (await world.CountAsync(owner, ItemCatalog.Wood)).Should().Be(0);
        (await world.Inventory.PeekSlotAsync(ContainerIds.ForChest(chest), 3)).Should().Be(new InventorySlotView(3, ItemCatalog.Wood, 60));
        await using NosumDbContext db = world.Db();
        ChestLogEntry log = await db.ChestLog.SingleAsync(e => e.ObjectId == chest);
        (log.PlayerId, log.ItemId, log.Delta, log.Reason).Should().Be((owner, ItemCatalog.Wood, 60, "chest.deposit"));
        (await db.InventoryLog.Where(e => e.PlayerId == owner && e.Reason == "chest.deposit").SingleAsync()).Delta.Should().Be(-60);
        (await db.InventoryLog.AnyAsync(e => e.PlayerId == ContainerIds.ForChest(chest))).Should().BeFalse("a container has no player to log against");
        ChestUpdate push = world.Presence.Sent<ChestUpdate>(owner).Single();
        push.ObjectId.Should().Be(chest);
        push.Slots.Should().HaveCount(36);
        push.Slots.Single(s => s.Slot == 3).Should().BeEquivalentTo(new InventorySlotRow { Slot = 3, ItemId = ItemCatalog.Wood, Quantity = 60 });
        push.Slots.Count(s => s.ItemId == 0).Should().Be(35);
    }

    [Fact]
    public async Task Move_PartOfAStack_ThenMoreOntoIt_MergesUpToTheLimit()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int slot = await world.GiveAsync(owner, ItemCatalog.Wood, 99);
        _ = await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        Guid chestOwner = ContainerIds.ForChest(chest);

        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 40)).Should().Be(ChestStatus.Ok);
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 30)).Should().Be(ChestStatus.Ok);
        (await world.Inventory.PeekSlotAsync(chestOwner, 0)).Quantity.Should().Be(70);
        (await world.CountAsync(owner, ItemCatalog.Wood)).Should().Be(29);

        // Only 29 more fit on a stack of 99: the move takes what fits and leaves the rest.
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.Ok);
        (await world.Inventory.PeekSlotAsync(chestOwner, 0)).Quantity.Should().Be(99);
        (await world.CountAsync(owner, ItemCatalog.Wood)).Should().Be(0);
        await using NosumDbContext db = world.Db();
        (await db.ChestLog.Where(e => e.ObjectId == chest).SumAsync(e => e.Delta)).Should().Be(99, "the log adds up to what is inside");
    }

    [Fact]
    public async Task Move_QuickMove_GoesToTheFirstSlotThatFits_AndWithdrawalReturnsTheItems()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int slot = await world.GiveAsync(owner, ItemCatalog.Stone, 20);
        _ = await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);

        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, -1, 0)).Should().Be(ChestStatus.Ok);
        (await world.Inventory.PeekSlotAsync(ContainerIds.ForChest(chest), 0)).Should().Be(new InventorySlotView(0, ItemCatalog.Stone, 20));

        (await world.Chests.MoveAsync(me, chest, ChestSide.Chest, 0, ChestSide.Player, -1, 8)).Should().Be(ChestStatus.Ok);
        (await world.CountAsync(owner, ItemCatalog.Stone)).Should().Be(8);
        (await world.CountInChestAsync(chest, ItemCatalog.Stone)).Should().Be(12);

        (await world.Chests.MoveAsync(me, chest, ChestSide.Chest, 0, ChestSide.Player, -1, 0)).Should().Be(ChestStatus.Ok);
        (await world.CountAsync(owner, ItemCatalog.Stone)).Should().Be(20);
        (await world.CountInChestAsync(chest, ItemCatalog.Stone)).Should().Be(0);
        await using NosumDbContext db = world.Db();
        (await db.ChestLog.Where(e => e.ObjectId == chest).OrderBy(e => e.OccurredAt).Select(e => e.Delta).ToListAsync()).Sum().Should().Be(0);
        (await db.ChestLog.CountAsync(e => e.ObjectId == chest && e.Reason == "chest.withdraw")).Should().Be(2);
    }

    [Fact]
    public async Task Move_ABagStackOntoAChestStackOfAnotherItem_SwapsThemWhole()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int woodSlot = await world.GiveAsync(owner, ItemCatalog.Wood, 30);
        int stoneSlot = await world.GiveAsync(owner, ItemCatalog.Stone, 12);
        _ = await world.Chests.OpenAsync(world.Presence.ConnectionOf(owner), chest);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        _ = await world.Chests.MoveAsync(me, chest, ChestSide.Player, woodSlot, ChestSide.Chest, 5, 0);

        ChestStatus partial = await world.Chests.MoveAsync(me, chest, ChestSide.Player, stoneSlot, ChestSide.Chest, 5, 5);
        ChestStatus swapped = await world.Chests.MoveAsync(me, chest, ChestSide.Player, stoneSlot, ChestSide.Chest, 5, 0);

        partial.Should().Be(ChestStatus.Blocked, "a part of a stack cannot swap with another item");
        swapped.Should().Be(ChestStatus.Ok);
        (await world.Inventory.PeekSlotAsync(ContainerIds.ForChest(chest), 5)).Should().Be(new InventorySlotView(5, ItemCatalog.Stone, 12));
        (await world.Inventory.PeekSlotAsync(owner, stoneSlot)).Should().Be(new InventorySlotView(stoneSlot, ItemCatalog.Wood, 30), "the wood took the slot the stone left");
        (await world.CountAsync(owner, ItemCatalog.Stone)).Should().Be(0);
        await using NosumDbContext db = world.Db();
        List<ChestLogEntry> log = await db.ChestLog.Where(e => e.ObjectId == chest).ToListAsync();
        log.Where(e => e.ItemId == ItemCatalog.Wood).Sum(e => e.Delta).Should().Be(0, "the wood went in and out again");
        log.Where(e => e.ItemId == ItemCatalog.Stone).Sum(e => e.Delta).Should().Be(12);
    }

    [Fact]
    public async Task Move_Refusals_ChangeNothing()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int slot = await world.GiveAsync(owner, ItemCatalog.Wood, 10);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);

        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.NotOpen, "the chest is opened first");
        _ = await world.Chests.OpenAsync(me, chest);
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, 30, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.EmptySlot);
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 11)).Should().Be(ChestStatus.InvalidQuantity);
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 36, 0)).Should().Be(ChestStatus.InvalidSlot);
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Player, 0, 0)).Should().Be(ChestStatus.InvalidSlot, "inside the bag the inventory's own move does the work");
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, 0, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.NotAllowed, "the hoe is bound to its owner and never goes in a chest");
        (await world.Chests.MoveAsync(me, chest, ChestSide.Chest, 0, ChestSide.Player, -1, 0)).Should().Be(ChestStatus.EmptySlot);

        (await world.CountAsync(owner, ItemCatalog.Wood)).Should().Be(10);
        (await world.CountAsync(owner, ItemCatalog.Hoe)).Should().Be(1);
        await using NosumDbContext db = world.Db();
        (await db.ChestLog.AnyAsync(e => e.ObjectId == chest)).Should().BeFalse();
    }

    [Fact]
    public async Task Move_IntoAFullChest_IsRefused_AndNothingIsLost()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        _ = await world.Chests.OpenAsync(me, chest);
        for (int i = 0; i < 36; i++)
        {
            int slot = await world.GiveAsync(owner, ItemCatalog.Fiber, 99);
            (await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, -1, 0)).Should().Be(ChestStatus.Ok);
        }

        int last = await world.GiveAsync(owner, ItemCatalog.Stone, 5);

        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, last, ChestSide.Chest, -1, 0)).Should().Be(ChestStatus.NoSpace);
        (await world.CountAsync(owner, ItemCatalog.Stone)).Should().Be(5);
        (await world.CountInChestAsync(chest, ItemCatalog.Fiber)).Should().Be(36 * 99);
    }

    [Fact]
    public async Task Move_InsideTheChest_Reorders_WithoutAnAuditLine()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int slot = await world.GiveAsync(owner, ItemCatalog.Wood, 5);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        _ = await world.Chests.OpenAsync(me, chest);
        _ = await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0);

        (await world.Chests.MoveAsync(me, chest, ChestSide.Chest, 0, ChestSide.Chest, 20, 0)).Should().Be(ChestStatus.Ok);

        (await world.Inventory.PeekSlotAsync(ContainerIds.ForChest(chest), 20)).Quantity.Should().Be(5);
        await using NosumDbContext db = world.Db();
        (await db.ChestLog.CountAsync(e => e.ObjectId == chest)).Should().Be(1, "only the deposit is logged");
    }

    [Fact]
    public async Task Move_TwoMembersTakingTheSameStack_ExactlyOneGetsIt()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid member = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, member);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        (int x, int y, _) = world.OpenRow(GameMaps.CoopFarm, 4);
        world.Stand(founder, land, x + 1, y);
        world.Stand(member, land, x + 1, y);
        int chest = await world.PlaceOnFarmAsync(founder, ItemCatalog.Chest, x, y);
        Nalix.Abstractions.Networking.IConnection a = world.Presence.ConnectionOf(founder);
        Nalix.Abstractions.Networking.IConnection b = world.Presence.ConnectionOf(member);
        _ = await world.Chests.OpenAsync(a, chest);
        _ = await world.Chests.OpenAsync(b, chest);
        int slot = await world.GiveAsync(founder, ItemCatalog.Wood, 50);
        _ = await world.Chests.MoveAsync(a, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0);

        for (int round = 0; round < 6; round++)
        {
            ChestStatus[] results = await Task.WhenAll(
                Task.Run(() => world.Chests.MoveAsync(a, chest, ChestSide.Chest, 0, ChestSide.Player, -1, 0)),
                Task.Run(() => world.Chests.MoveAsync(b, chest, ChestSide.Chest, 0, ChestSide.Player, -1, 0)));

            results.Count(r => r == ChestStatus.Ok).Should().Be(1, "round " + round);
            results.Single(r => r != ChestStatus.Ok).Should().Be(ChestStatus.EmptySlot);
            (await world.CountInChestAsync(chest, ItemCatalog.Wood)).Should().Be(0);
            (await world.CountAsync(founder, ItemCatalog.Wood) + await world.CountAsync(member, ItemCatalog.Wood)).Should().Be(50, "the stack is in exactly one place");

            // Put it back for the next round, from whoever holds it.
            Guid holder = await world.CountAsync(founder, ItemCatalog.Wood) > 0 ? founder : member;
            int holding = (await world.Inventory.GetAsync(holder)).Slots.First(s => s.ItemId == ItemCatalog.Wood).Slot;
            (await world.Chests.MoveAsync(world.Presence.ConnectionOf(holder), chest, ChestSide.Player, holding, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.Ok);
        }

        await using NosumDbContext db = world.Db();
        (await db.ChestLog.Where(e => e.ObjectId == chest).SumAsync(e => e.Delta)).Should().Be(50);
    }

    [Fact]
    public async Task Move_ADepositRacingThePickUpOfTheChest_NeverLosesItems()
    {
        using PlacementHarness world = new(_database);
        for (int round = 0; round < 8; round++)
        {
            (Guid owner, int chest, _, _) = await FarmChestAsync(world);
            int slot = await world.GiveAsync(owner, ItemCatalog.Wood, 10);
            Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
            _ = await world.Chests.OpenAsync(me, chest);

            Task<ChestStatus> deposit = Task.Run(() => world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0));
            Task<PlaceOutcome> pickUp = Task.Run(() => world.Placeables.PickUpAsync(me, chest));
            await Task.WhenAll(deposit, pickUp);

            bool stillThere = (await world.Placeables.FindAsync(new MapInstance(GameMaps.Farm, owner), chest)) is not null;
            int inChest = await world.CountInChestAsync(chest, ItemCatalog.Wood);
            int inBag = await world.CountAsync(owner, ItemCatalog.Wood);
            (inChest + inBag).Should().Be(10, $"round {round}: deposit {await deposit}, pick-up {(await pickUp).Status}");
            if (stillThere)
            {
                (await pickUp).Status.Should().Be(PlaceStatus.NotEmpty, "a chest that got the wood cannot be picked up");
                inChest.Should().Be(10);
            }
            else
            {
                (await deposit).Should().NotBe(ChestStatus.Ok, "a chest that was picked up first takes nothing");
                inChest.Should().Be(0);
                inBag.Should().Be(10);
                (await world.CountAsync(owner, ItemCatalog.Chest)).Should().Be(1);
            }
        }
    }

    [Fact]
    public async Task PickUp_AChestWithItems_IsRefused_AnEmptyOneComes_AndItsViewersAreToldToClose()
    {
        using PlacementHarness world = new(_database);
        (Guid owner, int chest, _, _) = await FarmChestAsync(world);
        int slot = await world.GiveAsync(owner, ItemCatalog.Wood, 7);
        Nalix.Abstractions.Networking.IConnection me = world.Presence.ConnectionOf(owner);
        _ = await world.Chests.OpenAsync(me, chest);
        _ = await world.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0);

        (await world.Placeables.PickUpAsync(me, chest)).Should().Be(new PlaceOutcome(PlaceStatus.NotEmpty, 0));
        (await world.CountAsync(owner, ItemCatalog.Chest)).Should().Be(0);

        _ = await world.Chests.MoveAsync(me, chest, ChestSide.Chest, 0, ChestSide.Player, -1, 0);
        (await world.Placeables.PickUpAsync(me, chest)).Should().Be(new PlaceOutcome(PlaceStatus.Ok, chest));

        (await world.CountAsync(owner, ItemCatalog.Chest)).Should().Be(1);
        world.Presence.Sent<ChestClosed>(owner).Should().ContainSingle().Which.ObjectId.Should().Be(chest);
        await using NosumDbContext db = world.Db();
        (await db.PlayerInventories.AnyAsync(i => i.PlayerId == ContainerIds.ForChest(chest))).Should().BeFalse("the empty chest's inventory went with it");
        (await db.PlacedObjects.AnyAsync(p => p.Id == chest)).Should().BeFalse();
        (await world.Chests.MoveAsync(me, chest, ChestSide.Player, 0, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.UnknownObject, "there is no chest to move into any more");
    }

    [Fact]
    public async Task Chest_KeepsItsItemsWhenMoved_AndAcrossARestartOfTheServices()
    {
        Guid owner;
        int chest;
        int x;
        int y;
        using (PlacementHarness first = new(_database))
        {
            (owner, chest, x, y) = await FarmChestAsync(first);
            int slot = await first.GiveAsync(owner, ItemCatalog.Coal, 12);
            Nalix.Abstractions.Networking.IConnection me = first.Presence.ConnectionOf(owner);
            _ = await first.Chests.OpenAsync(me, chest);
            _ = await first.Chests.MoveAsync(me, chest, ChestSide.Player, slot, ChestSide.Chest, 9, 0);
            (await first.Placeables.MoveAsync(me, chest, x + 2, y, 0)).Status.Should().Be(PlaceStatus.Ok, "a chest with items can be shoved aside");
        }

        using PlacementHarness second = new(_database);
        second.StandOnFarm(owner, x + 2, y + 1);
        Nalix.Abstractions.Networking.IConnection again = second.Presence.ConnectionOf(owner);

        ChestOpenOutcome opened = await second.Chests.OpenAsync(again, chest);

        opened.Status.Should().Be(ChestStatus.Ok);
        opened.Slots.Should().ContainSingle().Which.Should().Be(new InventorySlotView(9, ItemCatalog.Coal, 12));
    }

    [Fact]
    public async Task CoopLand_MembersOpenTheChest_AStrangerAndARemovedMemberDoNot_AndAWalkerStopsWatching()
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

        int chest = await world.PlaceOnFarmAsync(founder, ItemCatalog.Chest, x, y);
        int slot = await world.GiveAsync(founder, ItemCatalog.Stone, 9);

        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(stranger), chest)).Status.Should().Be(ChestStatus.NotYours);
        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(founder), chest)).Status.Should().Be(ChestStatus.Ok);
        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(member), chest)).Status.Should().Be(ChestStatus.Ok);
        (await world.Chests.MoveAsync(world.Presence.ConnectionOf(founder), chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0)).Should().Be(ChestStatus.Ok);
        world.Presence.Sent<ChestUpdate>(member).Should().ContainSingle("the other member watching sees the change");

        // The member walks away: no more pushes to them.
        await world.Presence.LeaveAsync(member);
        _ = await world.Chests.MoveAsync(world.Presence.ConnectionOf(founder), chest, ChestSide.Chest, 0, ChestSide.Player, -1, 3);
        world.Presence.Sent<ChestUpdate>(member).Should().ContainSingle("a player who left no longer watches");

        // A removed member is refused at once, even standing next to it.
        world.Stand(member, land, x + 1, y);
        await using (NosumDbContext db = world.Db())
        {
            db.CoopMembers.RemoveRange(await db.CoopMembers.Where(m => m.PlayerId == member).ToListAsync());
            _ = await db.SaveChangesAsync();
        }

        (await world.Chests.OpenAsync(world.Presence.ConnectionOf(member), chest)).Status.Should().Be(ChestStatus.NotYours);
    }

    [Fact]
    public async Task CoopLand_ChestMovesAreLoggedPerPlayer()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid member = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, member);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        (int x, int y, _) = world.OpenRow(GameMaps.CoopFarm, 4);
        world.Stand(founder, land, x + 1, y);
        world.Stand(member, land, x + 1, y);
        int chest = await world.PlaceOnFarmAsync(founder, ItemCatalog.Chest, x, y);
        int slot = await world.GiveAsync(founder, ItemCatalog.Wood, 20);
        _ = await world.Chests.OpenAsync(world.Presence.ConnectionOf(founder), chest);
        _ = await world.Chests.OpenAsync(world.Presence.ConnectionOf(member), chest);

        _ = await world.Chests.MoveAsync(world.Presence.ConnectionOf(founder), chest, ChestSide.Player, slot, ChestSide.Chest, 0, 0);
        _ = await world.Chests.MoveAsync(world.Presence.ConnectionOf(member), chest, ChestSide.Chest, 0, ChestSide.Player, -1, 8);

        await using NosumDbContext db = world.Db();
        List<ChestLogEntry> log = await db.ChestLog.Where(e => e.ObjectId == chest).OrderBy(e => e.OccurredAt).ToListAsync();
        log.Select(e => (e.PlayerId, e.Delta, e.Reason)).Should().Equal((founder, 20, "chest.deposit"), (member, -8, "chest.withdraw"));
        (await db.CoopLog.Where(e => e.CoopId == coop).Select(e => e.Reason).ToListAsync()).Should().Equal(CoopLand.PlaceReason);
    }

    // The owner on their own farm with a chest placed next to them.
    private static async Task<(Guid Owner, int Chest, int X, int Y)> FarmChestAsync(PlacementHarness world)
    {
        Guid owner = await world.CreatePlayerAsync();
        (int x, int y, _) = world.OpenRow(GameMaps.Farm, 4);
        world.StandOnFarm(owner, x + 1, y);
        int chest = await world.PlaceOnFarmAsync(owner, ItemCatalog.Chest, x, y);
        return (owner, chest, x, y);
    }
}
