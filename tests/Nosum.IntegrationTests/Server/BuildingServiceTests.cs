// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Buildings;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Domain.Buildings;
using Nosum.Domain.Coops;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Farm buildings and animals (docs/adr/ADR-028) against a real PostgreSQL database with a clock the test moves: the carpenter's counter, construction over two game days, the
/// door and interior access, buying, naming and housing animals, feeding, the next day's produce collected exactly once, petting, and that a visitor may look but not tend.
/// </summary>
public sealed class BuildingServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly long s_dayMs = Nosum.Gameplay.Calendar.GameCalendar.DefaultDayLengthMs;

    private readonly PostgresFixture _database;

    public BuildingServiceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Commission_AtTheCounter_TakesCoinsWoodAndStoneAndHandsOverTheBlueprint()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        await world.GiveCoinsAsync(owner, 4_000);
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Wood, 320, "test.setup");
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Stone, 130, "test.setup");
        world.StandAtCarpenter(owner);
        long before = await world.CoinsOfAsync(owner);

        BuildingOutcome outcome = await world.Buildings.CommissionAsync(world.Presence.ConnectionOf(owner), BuildingKind.Coop);

        outcome.Should().Be(new BuildingOutcome(BuildingStatus.Ok, Guid.Empty, ItemCatalog.CoopBlueprint, 1));
        (await world.CoinsOfAsync(owner)).Should().Be(before - 4_000);
        (await world.CountAsync(owner, ItemCatalog.Wood)).Should().Be(20);
        (await world.CountAsync(owner, ItemCatalog.Stone)).Should().Be(30);
        (await world.CountAsync(owner, ItemCatalog.CoopBlueprint)).Should().Be(1);
    }

    [Fact]
    public async Task Commission_Refusals_ChangeNothing()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        IConnection me = world.Presence.ConnectionOf(await Place(world, owner));
        await world.GiveCoinsAsync(owner, 3_499);
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Wood, 300, "test.setup");
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Stone, 100, "test.setup");

        world.StandOnFarm(owner, 64, 17);
        (await world.Buildings.CommissionAsync(me, BuildingKind.Coop)).Status.Should().Be(BuildingStatus.NotAtShop, "the counter is in Town");
        world.StandAtCarpenter(owner);
        (await world.Buildings.CommissionAsync(me, BuildingKind.Coop)).Status.Should().Be(BuildingStatus.NotEnoughCoins, "one coin short");
        (await world.Buildings.CommissionAsync(me, BuildingKind.None)).Status.Should().Be(BuildingStatus.Unknown);
        await world.GiveCoinsAsync(owner, 1);
        await world.Inventory.RemoveAsync(owner, ItemCatalog.Stone, 1, "test.setup");
        (await world.Buildings.CommissionAsync(me, BuildingKind.Coop)).Status.Should().Be(BuildingStatus.NotEnoughItems, "99 stone are too few");

        (await world.CoinsOfAsync(owner)).Should().Be(500 + 3_499 + 1);
        (await world.CountAsync(owner, ItemCatalog.Wood)).Should().Be(300);
        (await world.CountAsync(owner, ItemCatalog.CoopBlueprint)).Should().Be(0);
    }

    [Fact]
    public async Task Place_Blueprint_StartsConstruction_FinishedTwoGameDaysLater_AndTheBuildingIsAddedInTheSameTransaction()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        await world.GiveCoinsAsync(owner, 4_000);
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Wood, 300, "test.setup");
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Stone, 100, "test.setup");
        world.StandAtCarpenter(owner);
        _ = await world.Buildings.CommissionAsync(world.Presence.ConnectionOf(owner), BuildingKind.Coop);
        (int x, int y) = world.OpenArea(GameMaps.Farm, 6, 3);
        world.StandOnFarm(owner, x + 1, y + 3);
        int slot = (await world.Inventory.GetAsync(owner)).Slots.First(s => s.ItemId == ItemCatalog.CoopBlueprint).Slot;

        Nosum.Abstractions.Placeables.PlaceOutcome placed = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(owner), slot, x, y, 0);

        placed.Status.Should().Be(PlaceStatus.Ok);
        (await world.CountAsync(owner, ItemCatalog.CoopBlueprint)).Should().Be(0, "placing consumed the blueprint");
        await using NosumDbContext db = world.Db();
        FarmBuilding building = await db.FarmBuildings.SingleAsync(b => b.ObjectId == placed.ObjectId);
        building.Kind.Should().Be((byte)BuildingKind.Coop);
        (building.MapId, building.OwnerId).Should().Be((GameMaps.Farm, owner));
        building.CompletesAtMs.Should().Be(PlacementHarness.Start.ToUnixTimeMilliseconds() + (2 * s_dayMs));
        BuildingsSnapshot pushed = world.Presence.Sent<BuildingsSnapshot>().Single();
        pushed.Buildings.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = building.Id, ObjectId = placed.ObjectId, Kind = (byte)BuildingKind.Coop, MapId = GameMaps.Farm, RemainingMs = (int)(2 * s_dayMs), Animals = (byte)0, Capacity = (byte)4 });

        world.Clock.Advance(TimeSpan.FromMilliseconds((2 * s_dayMs) - 1));
        (await world.Buildings.ListAsync(world.Presence.ConnectionOf(owner), mine: true)).Single().RemainingMs.Should().Be(1);
        world.Clock.Advance(TimeSpan.FromMilliseconds(1));
        (await world.Buildings.ListAsync(world.Presence.ConnectionOf(owner), mine: true)).Single().RemainingMs.Should().Be(0);
    }

    [Fact]
    public async Task Place_BlueprintWithANodeInFrontOfItsDoor_IsRefused_SoTheDoorCanBeReached()
    {
        using PlacementHarness probe = new(_database);
        (int x, int y) = probe.OpenArea(GameMaps.Farm, 6, 4);
        using PlacementHarness world = new(_database, nodes: new PlacementHarness.NodesAt((x + 1, y + 3)));
        Guid owner = await world.CreatePlayerAsync();
        await world.GiveCoinsAsync(owner, 4_000);
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Wood, 300, "test.setup");
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Stone, 100, "test.setup");
        world.StandAtCarpenter(owner);
        _ = await world.Buildings.CommissionAsync(world.Presence.ConnectionOf(owner), BuildingKind.Coop);
        world.StandOnFarm(owner, x + 2, y + 3);
        int slot = (await world.Inventory.GetAsync(owner)).Slots.First(s => s.ItemId == ItemCatalog.CoopBlueprint).Slot;

        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(owner), slot, x, y, 0)).Status.Should().Be(PlaceStatus.NodeInTheWay, "the node stands on the tile in front of the door");
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(owner), slot, x + 1, y, 0)).Status.Should().Be(PlaceStatus.Ok, "one tile to the right the doorstep is free");
    }

    [Fact]
    public async Task Place_BlueprintInTheFarmhouse_OrOverAFence_IsRefused_AndAFinishedBuildingCannotBeMovedOrPickedUp()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        IConnection me = world.Presence.ConnectionOf(await Place(world, owner));
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.CoopBlueprint, 1, "test.setup");
        int slot = (await world.Inventory.GetAsync(owner)).Slots.First(s => s.ItemId == ItemCatalog.CoopBlueprint).Slot;

        // The farmhouse takes furniture, not buildings.
        world.Presence.Stand(owner, new MapInstance(GameMaps.FarmHouse, owner), 3 * 16, 6 * 16);
        (await world.Placeables.PlaceAsync(me, slot, 2, 3, 0)).Status.Should().Be(PlaceStatus.NotPlaceable);

        // A fence on the tile in front of the door means the door could not be used.
        (int x, int y) = world.OpenArea(GameMaps.Farm, 6, 3);
        world.StandOnFarm(owner, x + 1, y + 4);
        _ = await world.PlaceOnFarmAsync(owner, ItemCatalog.WoodFence, x + 1, y + 3);
        (await world.Placeables.PlaceAsync(me, slot, x, y, 0)).Status.Should().Be(PlaceStatus.Blocked);
        (await world.CountAsync(owner, ItemCatalog.CoopBlueprint)).Should().Be(1);

        // A finished building stays where it is.
        BuildingInfo building = await world.BuildAsync(owner, BuildingKind.Coop, skip: 1);
        world.StandOnFarm(owner, building.TileX + 1, building.TileY + 3);
        (await world.Placeables.MoveAsync(me, building.ObjectId, building.TileX + 1, building.TileY, 0)).Status.Should().Be(PlaceStatus.Fixed);
        (await world.Placeables.PickUpAsync(me, building.ObjectId)).Status.Should().Be(PlaceStatus.Fixed);
        await using NosumDbContext db = world.Db();
        (await db.FarmBuildings.AnyAsync(b => b.Id == building.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Place_NinthBuildingOnOneLand_IsRefused()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        IConnection me = world.Presence.ConnectionOf(await Place(world, owner));
        for (int i = 0; i < BuildingRules.MaxPerLand; i++)
        {
            (int x, int y) = world.OpenArea(GameMaps.Farm, 6, 3, skip: i);
            world.StandOnFarm(owner, x + 1, y + 3);
            _ = await world.PlaceOnFarmAsync(owner, ItemCatalog.CoopBlueprint, x, y);
        }

        _ = await world.Inventory.AddAsync(owner, ItemCatalog.CoopBlueprint, 1, "test.setup");
        int slot = (await world.Inventory.GetAsync(owner)).Slots.First(s => s.ItemId == ItemCatalog.CoopBlueprint).Slot;
        (int nx, int ny) = world.OpenArea(GameMaps.Farm, 6, 3, skip: BuildingRules.MaxPerLand);
        world.StandOnFarm(owner, nx + 1, ny + 3);

        (await world.Placeables.PlaceAsync(me, slot, nx, ny, 0)).Status.Should().Be(PlaceStatus.LimitReached);
    }

    [Fact]
    public async Task Interior_IsEnteredOnlyWhenFinished_ByTheOwner_AndByVisitorsTheOwnerAllowed()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync(guest: false);
        Guid friend = await world.CreatePlayerAsync();
        Guid stranger = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        await GrantVisitAsync(world, owner, friend);
        BuildingInfo building = await world.BuildAsync(owner, BuildingKind.Coop);

        (await world.Access.CanEnterAsync(building.Interior, owner)).Should().BeTrue();
        (await world.Access.CanEnterAsync(building.Interior, friend)).Should().BeTrue("the owner let them visit the farm");
        (await world.Access.CanEnterAsync(building.Interior, stranger)).Should().BeFalse();
        (await world.Access.CanEnterAsync(new MapInstance("coop", Guid.NewGuid()), owner)).Should().BeFalse("no such building");
        (await world.Access.CanEnterAsync(new MapInstance("barn", building.Id), owner)).Should().BeFalse("it is a coop, not a barn");
        (await world.Access.CanEnterAsync(new MapInstance(GameMaps.Farm, owner), friend)).Should().BeTrue();
    }

    [Fact]
    public async Task Interior_OfABuildingStillUnderConstruction_IsClosedToEveryone()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        await world.GiveCoinsAsync(owner, 4_000);
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Wood, 300, "test.setup");
        _ = await world.Inventory.AddAsync(owner, ItemCatalog.Stone, 100, "test.setup");
        world.StandAtCarpenter(owner);
        _ = await world.Buildings.CommissionAsync(world.Presence.ConnectionOf(owner), BuildingKind.Coop);
        (int x, int y) = world.OpenArea(GameMaps.Farm, 6, 3);
        world.StandOnFarm(owner, x + 1, y + 3);
        int slot = (await world.Inventory.GetAsync(owner)).Slots.First(s => s.ItemId == ItemCatalog.CoopBlueprint).Slot;
        _ = await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(owner), slot, x, y, 0);
        BuildingInfo building = world.BuildingDirectory.BuildingsOf(new MapInstance(GameMaps.Farm, owner)).Single();

        (await world.Access.CanEnterAsync(building.Interior, owner)).Should().BeFalse();
        await world.GiveCoinsAsync(owner, 800);
        world.StandAtCarpenter(owner);
        (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(owner), building.Id, AnimalKind.Chicken, "Early")).Status.Should().Be(BuildingStatus.NotFinished);

        world.Clock.Advance(TimeSpan.FromMilliseconds(2 * s_dayMs));
        (await world.Access.CanEnterAsync(building.Interior, owner)).Should().BeTrue();
    }

    [Fact]
    public async Task BuyAnimal_AtTheCounter_NamesItAndPutsItInTheBuilding_ForCoins()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 800);
        long before = await world.CoinsOfAsync(owner);

        BuildingOutcome bought = await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(owner), coop.Id, AnimalKind.Chicken, "  Mi Mi ");

        bought.Status.Should().Be(BuildingStatus.Ok);
        bought.Id.Should().NotBe(Guid.Empty);
        (await world.CoinsOfAsync(owner)).Should().Be(before - 800);
        await using NosumDbContext db = world.Db();
        FarmAnimal animal = await db.FarmAnimals.SingleAsync(a => a.Id == bought.Id);
        (animal.BuildingId, animal.Kind, animal.Name, animal.Friendship).Should().Be((coop.Id, (byte)AnimalKind.Chicken, "Mi Mi", 0));
        AnimalsChanged pushed = world.Presence.Sent<AnimalsChanged>()[^1];
        pushed.Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = animal.Id, Name = "Mi Mi", Kind = (byte)AnimalKind.Chicken });
    }

    [Fact]
    public async Task BuyAnimal_Refusals_ChargeNothing()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        Guid stranger = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        _ = await Place(world, stranger);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 20_000);
        long before = await world.CoinsOfAsync(owner);
        IConnection me = world.Presence.ConnectionOf(owner);

        (await world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.Cow, "Bella")).Status.Should().Be(BuildingStatus.WrongResident, "cows live in barns");
        (await world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.Chicken, "ab")).Status.Should().Be(BuildingStatus.BadName);
        (await world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.Chicken, "fuck")).Status.Should().Be(BuildingStatus.BadName, "the profanity filter applies");
        (await world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.None, "Bella")).Status.Should().Be(BuildingStatus.Unknown);
        (await world.Buildings.BuyAnimalAsync(me, Guid.NewGuid(), AnimalKind.Chicken, "Bella")).Status.Should().Be(BuildingStatus.Unknown);
        world.StandOnFarm(owner, 64, 17);
        (await world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.Chicken, "Bella")).Status.Should().Be(BuildingStatus.NotAtShop);
        world.StandAtCarpenter(stranger);
        (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(stranger), coop.Id, AnimalKind.Chicken, "Bella")).Status.Should().Be(BuildingStatus.NotYours, "somebody else's coop");

        (await world.CoinsOfAsync(owner)).Should().Be(before);
        await using NosumDbContext db = world.Db();
        (await db.FarmAnimals.CountAsync(a => a.BuildingId == coop.Id)).Should().Be(0);
    }

    [Fact]
    public async Task BuyAnimal_ThePlaceAfterTheFourth_IsRefused_EvenWhenAskedAtOnce()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 10_000);
        IConnection me = world.Presence.ConnectionOf(owner);
        long before = await world.CoinsOfAsync(owner);

        BuildingOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.Chicken, "Hen " + i))));

        outcomes.Count(o => o.Status == BuildingStatus.Ok).Should().Be(4);
        outcomes.Where(o => o.Status != BuildingStatus.Ok).Should().OnlyContain(o => o.Status == BuildingStatus.Full);
        (await world.CoinsOfAsync(owner)).Should().Be(before - (4 * 800), "only the four that fit were paid for");
        BuildingView view = (await world.Buildings.ListAsync(me, mine: true)).Single();
        (view.Animals, view.Capacity).Should().Be((4, 4));
    }

    [Fact]
    public async Task Feed_PutsHayInTheBench_TakesItFromTheSlot_AndRefusesWhatIsNotFeedOrOutOfReach()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        BuildingDefinition definition = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Coop);
        IConnection me = world.Presence.ConnectionOf(owner);
        int hay = await world.GiveAsync(owner, ItemCatalog.Hay, 10);
        int stone = await world.GiveAsync(owner, ItemCatalog.Stone, 3);

        world.StandInside(owner, coop, definition.ArrivalX, definition.ArrivalY);
        (await world.Buildings.FeedAsync(me, hay, 4)).Status.Should().Be(BuildingStatus.OutOfReach, "the bench is across the room");
        world.StandInside(owner, coop, definition.TroughLeft + 1, definition.TroughTop + 1);
        (await world.Buildings.FeedAsync(me, stone, 1)).Status.Should().Be(BuildingStatus.NotFeed);
        (await world.Buildings.FeedAsync(me, hay, 0)).Status.Should().Be(BuildingStatus.NotFeed);

        BuildingOutcome fed = await world.Buildings.FeedAsync(me, hay, 4);

        fed.Should().Be(new BuildingOutcome(BuildingStatus.Ok, Guid.Empty, ItemCatalog.Hay, 4));
        (await world.CountAsync(owner, ItemCatalog.Hay)).Should().Be(6);
        world.Presence.Sent<AnimalsChanged>()[^1].Rations.Should().Be(4);

        // Fiber works too, and the bench holds only so much.
        int fiber = await world.GiveAsync(owner, ItemCatalog.Fiber, 99);
        (await world.Buildings.FeedAsync(me, hay, 6)).Quantity.Should().Be(10);
        (await world.Buildings.FeedAsync(me, fiber, 24)).Quantity.Should().Be(AnimalRules.MaxRations, "only 14 more fit");
        (await world.CountAsync(owner, ItemCatalog.Fiber)).Should().Be(99 - 14);
        (await world.Buildings.FeedAsync(me, fiber, 1)).Status.Should().Be(BuildingStatus.BenchFull);
    }

    [Fact]
    public async Task NextDay_AFedChickenHasAnEgg_CollectedExactlyOnce_EvenWhenAskedAtOnce()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 800);
        Guid hen = (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(owner), coop.Id, AnimalKind.Chicken, "Bella")).Id;
        BuildingDefinition definition = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Coop);
        IConnection me = world.Presence.ConnectionOf(owner);
        int hay = await world.GiveAsync(owner, ItemCatalog.Hay, 5);
        world.StandInside(owner, coop, definition.TroughLeft + 1, definition.TroughTop + 1);
        (await world.Buildings.FeedAsync(me, hay, 1)).Status.Should().Be(BuildingStatus.Ok);

        // Today there is nothing yet: it ate nothing last night.
        world.StandAt(owner, coop, hen);
        (await world.Buildings.CollectAsync(me, hen, 0)).Status.Should().Be(BuildingStatus.NoProduce);

        // Tomorrow: it ate at the start of the day, so it has produce.
        world.Clock.Advance(TimeSpan.FromMilliseconds(s_dayMs));
        AnimalRoomView room = (await world.Buildings.GetAnimalsAsync(me))!;
        room.Rations.Should().Be(0, "the hen ate the one ration overnight");
        room.Animals.Should().ContainSingle().Which.Flags.Should().Be(AnimalView.ProduceFlag);

        world.StandAt(owner, coop, hen);
        AnimalOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => world.Buildings.CollectAsync(me, hen, 0))));

        outcomes.Count(o => o.Status == BuildingStatus.Ok).Should().Be(1, "the produce is handed over once");
        outcomes.Where(o => o.Status != BuildingStatus.Ok).Should().OnlyContain(o => o.Status == BuildingStatus.NoProduce);
        AnimalOutcome collected = outcomes.Single(o => o.Status == BuildingStatus.Ok);
        collected.ItemId.Should().BeOneOf(ItemCatalog.Egg, ItemCatalog.LargeEgg);
        (await world.CountAsync(owner, collected.ItemId)).Should().Be(1);
        (await world.Buildings.GetAnimalsAsync(me))!.Animals.Single().Flags.Should().Be(0, "nothing left to collect today");

        // The day after, with nothing in the bench, the hen ate nothing and gives nothing.
        world.Clock.Advance(TimeSpan.FromMilliseconds(s_dayMs));
        world.StandAt(owner, coop, hen);
        (await world.Buildings.CollectAsync(me, hen, 0)).Status.Should().Be(BuildingStatus.NoProduce);
        definition.Capacity.Should().Be(4);
    }

    [Fact]
    public async Task Collect_OtherPlayersAnimal_FromAFarAnimal_OrWithTheWrongItem_IsRefused()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo barn = await world.BuildAsync(owner, BuildingKind.Barn);
        await world.GiveCoinsAsync(owner, 1_500);
        Guid cow = (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(owner), barn.Id, AnimalKind.Cow, "Rosie")).Id;
        BuildingDefinition definition = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Barn);
        IConnection me = world.Presence.ConnectionOf(owner);
        int hay = await world.GiveAsync(owner, ItemCatalog.Hay, 2);
        world.StandInside(owner, barn, definition.TroughLeft + 1, definition.TroughTop + 1);
        _ = await world.Buildings.FeedAsync(me, hay, 1);
        world.Clock.Advance(TimeSpan.FromMilliseconds(s_dayMs));
        int wood = await world.GiveAsync(owner, ItemCatalog.Wood, 1);
        int pail = await world.GiveAsync(owner, ItemCatalog.MilkPail, 1);

        // Far from the cow (the doorway is across the room from its area, at the far side of it), it is out of reach.
        world.StandInside(owner, barn, definition.ArrivalX, definition.ArrivalY + 1);
        AnimalMotionView where = world.Wander.Where(BuildingKind.Barn, cow, world.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        if (!AnimalRules.InReach(((definition.ArrivalX) * 16) + 8, ((definition.ArrivalY + 1) * 16) + 8, where.X, where.Y))
        {
            (await world.Buildings.PetAsync(me, cow)).Status.Should().Be(BuildingStatus.OutOfReach);
        }

        // A cow gives milk only to a pail in hand.
        world.StandAt(owner, barn, cow);
        (await world.Buildings.CollectAsync(me, cow, wood)).Status.Should().Be(BuildingStatus.NeedsPail);
        (await world.Buildings.CollectAsync(me, cow, 30)).Status.Should().Be(BuildingStatus.NeedsPail, "an empty slot is no pail");
        (await world.Buildings.CollectAsync(me, Guid.NewGuid(), pail)).Status.Should().Be(BuildingStatus.Unknown);
        AnimalOutcome milked = await world.Buildings.CollectAsync(me, cow, pail);
        milked.Status.Should().Be(BuildingStatus.Ok);
        milked.ItemId.Should().BeOneOf(ItemCatalog.Milk, ItemCatalog.LargeMilk);
        (await world.CountAsync(owner, ItemCatalog.MilkPail)).Should().Be(1, "the pail is a tool, not used up");
    }

    [Fact]
    public async Task Pet_OnceADay_RaisesFriendship_AndNextDayAgain()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 800);
        Guid hen = (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(owner), coop.Id, AnimalKind.Chicken, "Bella")).Id;
        IConnection me = world.Presence.ConnectionOf(owner);
        world.StandAt(owner, coop, hen);

        (await world.Buildings.PetAsync(me, hen)).Should().Be(new AnimalOutcome(BuildingStatus.Ok, 0, 0, AnimalRules.PettingGain));
        (await world.Buildings.PetAsync(me, hen)).Status.Should().Be(BuildingStatus.AlreadyPetted);
        (await world.Buildings.GetAnimalsAsync(me))!.Animals.Single().Flags.Should().Be(AnimalView.PettedFlag);

        world.Clock.Advance(TimeSpan.FromMilliseconds(s_dayMs));
        world.StandAt(owner, coop, hen);
        (await world.Buildings.PetAsync(me, hen)).Friendship.Should().Be(2 * AnimalRules.PettingGain);
        await using NosumDbContext db = world.Db();
        (await db.FarmAnimals.SingleAsync(a => a.Id == hen)).Friendship.Should().Be(80);
    }

    [Fact]
    public async Task Visitor_InsideAFriendsCoop_MayLook_ButNotFeedPetOrCollect()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync(guest: false);
        Guid visitor = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        await GrantVisitAsync(world, owner, visitor);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 800);
        Guid hen = (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(owner), coop.Id, AnimalKind.Chicken, "Bella")).Id;
        IConnection friend = world.Presence.ConnectionOf(owner);
        BuildingDefinition definition = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Coop);
        int hay = await world.GiveAsync(owner, ItemCatalog.Hay, 3);
        world.StandInside(owner, coop, definition.TroughLeft + 1, definition.TroughTop + 1);
        _ = await world.Buildings.FeedAsync(friend, hay, 1);
        world.Clock.Advance(TimeSpan.FromMilliseconds(s_dayMs));

        int visitorHay = await world.GiveAsync(visitor, ItemCatalog.Hay, 3);
        world.StandInside(visitor, coop, definition.TroughLeft + 1, definition.TroughTop + 1);
        IConnection visitorConnection = world.Presence.ConnectionOf(visitor);
        AnimalRoomView room = (await world.Buildings.GetAnimalsAsync(visitorConnection))!;

        room.MayTend.Should().BeFalse();
        room.Animals.Should().ContainSingle().Which.Flags.Should().Be(AnimalView.ProduceFlag, "they can see the egg");
        world.StandAt(visitor, coop, hen);
        (await world.Buildings.CollectAsync(visitorConnection, hen, 0)).Status.Should().Be(BuildingStatus.NotYours);
        (await world.Buildings.PetAsync(visitorConnection, hen)).Status.Should().Be(BuildingStatus.NotYours);
        world.StandInside(visitor, coop, definition.TroughLeft + 1, definition.TroughTop + 1);
        (await world.Buildings.FeedAsync(visitorConnection, visitorHay, 1)).Status.Should().Be(BuildingStatus.NotYours);
        (await world.CountAsync(visitor, ItemCatalog.Hay)).Should().Be(3);
        (await world.CountAsync(visitor, ItemCatalog.Egg) + await world.CountAsync(visitor, ItemCatalog.LargeEgg)).Should().Be(0);

        // The owner still finds the egg where it was.
        world.StandAt(owner, coop, hen);
        (await world.Buildings.CollectAsync(friend, hen, 0)).Status.Should().Be(BuildingStatus.Ok);
    }

    [Fact]
    public async Task Animals_OutsideABuilding_AreNobodysToTend()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        IConnection me = world.Presence.ConnectionOf(owner);

        (await world.Buildings.GetAnimalsAsync(me)).Should().BeNull("the farm is not a building");
        (await world.Buildings.FeedAsync(me, 0, 1)).Status.Should().Be(BuildingStatus.NotInside);
        (await world.Buildings.PetAsync(me, Guid.NewGuid())).Status.Should().Be(BuildingStatus.NotInside);
        (await world.Buildings.CollectAsync(me, Guid.NewGuid(), 0)).Status.Should().Be(BuildingStatus.NotInside);
    }

    [Fact]
    public async Task CoopLand_Buildings_AreTendedByTheMembers_AndLeaveAnAuditLine()
    {
        using PlacementHarness world = new(_database);
        Guid founder = await world.CreatePlayerAsync();
        Guid member = await world.CreatePlayerAsync();
        Guid outsider = await world.CreatePlayerAsync();
        Guid coop = await world.CreateCoopAsync(founder, member);
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);
        BuildingDefinition definition = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Coop);
        (int x, int y) = world.OpenArea(GameMaps.CoopFarm, 6, 3);
        world.Presence.Stand(founder, land, (x + 1) * 16 + 8, (y + 3) * 16 + 8);
        int blueprint = await world.GiveAsync(founder, ItemCatalog.CoopBlueprint, 1);
        (await world.Placeables.PlaceAsync(world.Presence.ConnectionOf(founder), blueprint, x, y, 0)).Status.Should().Be(PlaceStatus.Ok);
        world.Clock.Advance(TimeSpan.FromMilliseconds(2 * s_dayMs));
        BuildingInfo building = world.BuildingDirectory.BuildingsOf(land).Single();

        (await world.Access.CanEnterAsync(building.Interior, member)).Should().BeTrue();
        (await world.Access.CanEnterAsync(building.Interior, outsider)).Should().BeFalse();

        // A member buys the hen at the counter, feeds it; the hay they put in is in the audit log.
        await world.GiveCoinsAsync(member, 800);
        world.StandAtCarpenter(member);
        Guid hen = (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(member), building.Id, AnimalKind.Chicken, "Coopie")).Id;
        int hay = await world.GiveAsync(member, ItemCatalog.Hay, 2);
        world.StandInside(member, building, definition.TroughLeft + 1, definition.TroughTop + 1);
        (await world.Buildings.FeedAsync(world.Presence.ConnectionOf(member), hay, 1)).Status.Should().Be(BuildingStatus.Ok);
        world.Clock.Advance(TimeSpan.FromMilliseconds(s_dayMs));
        world.StandAt(founder, building, hen);
        (await world.Buildings.CollectAsync(world.Presence.ConnectionOf(founder), hen, 0)).Status.Should().Be(BuildingStatus.Ok);

        await using NosumDbContext db = world.Db();
        List<CoopLogEntry> log = await db.CoopLog.Where(e => e.CoopId == coop && (e.Reason == CoopLand.FeedReason || e.Reason == CoopLand.CollectReason)).ToListAsync();
        log.Should().HaveCount(2);
        log.Single(e => e.Reason == CoopLand.FeedReason).Should().BeEquivalentTo(new { ItemId = ItemCatalog.Hay, Delta = -1 });
        log.Single(e => e.Reason == CoopLand.CollectReason).Delta.Should().Be(1);

        // An outsider who is not in the co-op cannot buy for it.
        await world.GiveCoinsAsync(outsider, 800);
        world.StandAtCarpenter(outsider);
        (await world.Buildings.BuyAnimalAsync(world.Presence.ConnectionOf(outsider), building.Id, AnimalKind.Chicken, "Nope")).Status.Should().Be(BuildingStatus.NotYours);
    }

    [Fact]
    public async Task Wander_OccupiedRoom_ChoosesWalksInsideTheAreaOnWalkableTiles_AndTellsTheRoom_AnEmptyRoomIsLeftAlone()
    {
        using PlacementHarness world = new(_database);
        Guid owner = await world.CreatePlayerAsync();
        _ = await Place(world, owner);
        BuildingInfo coop = await world.BuildAsync(owner, BuildingKind.Coop);
        await world.GiveCoinsAsync(owner, 3_200);
        IConnection me = world.Presence.ConnectionOf(owner);
        List<Guid> hens = [];
        for (int i = 0; i < 4; i++)
        {
            hens.Add((await world.Buildings.BuyAnimalAsync(me, coop.Id, AnimalKind.Chicken, "Hen " + i)).Id);
        }

        BuildingDefinition definition = BuildingCatalog.All.Single(b => b.Kind == BuildingKind.Coop);
        Nosum.Abstractions.Maps.MapDefinition map = world.Map("coop");
        long now = world.Clock.GetUtcNow().ToUnixTimeMilliseconds();

        // Nobody inside: nothing is chosen.
        await world.Wander.TickAsync(now + 10_000);
        world.Presence.Sent<AnimalsMoved>().Should().BeEmpty();

        // The new animals stand on tiles of their area, not on the bench.
        foreach (Guid hen in hens)
        {
            AnimalMotionView at = world.Wander.Where(BuildingKind.Coop, hen, now);
            (int tx, int ty) = ((int)(at.X / 16), (int)(at.Y / 16));
            definition.InWanderArea(tx, ty).Should().BeTrue();
            definition.IsTrough(tx, ty).Should().BeFalse();
            map.Grid.IsBlocked(tx, ty).Should().BeFalse();
            at.MoveMs.Should().Be(0);
        }

        // Somebody inside: after a few seconds every animal that is due picks a straight walk.
        world.StandInside(owner, coop, definition.ArrivalX, definition.ArrivalY);
        for (int step = 1; step <= 6; step++)
        {
            await world.Wander.TickAsync(now + (step * 5_000L));
        }

        IReadOnlyList<AnimalsMoved> pushes = world.Presence.Sent<AnimalsMoved>();
        pushes.Should().NotBeEmpty();
        pushes.Should().OnlyContain(p => p.BuildingId == coop.Id);
        foreach (AnimalMoveRow move in pushes.SelectMany(p => p.Moves))
        {
            hens.Should().Contain(move.Id);
            move.DurationMs.Should().BePositive();
            float length = MathF.Sqrt(((move.ToX - move.FromX) * (move.ToX - move.FromX)) + ((move.ToY - move.FromY) * (move.ToY - move.FromY)));
            length.Should().BeInRange(16f, 6 * 16f + 1f);
            ((int)(move.ToX / 16), (int)(move.ToY / 16)).Should().Match<(int X, int Y)>(t => definition.InWanderArea(t.X, t.Y) && !definition.IsTrough(t.X, t.Y) && !map.Grid.IsBlocked(t.X, t.Y));
            (move.DurationMs / 1000f * 22f).Should().BeApproximately(length, 1f, "a chicken walks 22 pixels a second");
        }

        // Walking animals are computed from the walk, not ticked: halfway through, they are halfway there.
        AnimalMoveRow first = pushes.SelectMany(p => p.Moves).First();
        AnimalMotionView later = world.Wander.Where(BuildingKind.Coop, first.Id, now + (6 * 5_000L));
        (later.X, later.Y).Should().NotBe((0f, 0f));
    }

    [Fact]
    public async Task Buildings_AndTheirAnimals_SurviveARestartOfTheServices()
    {
        Guid owner;
        Guid hen;
        Guid buildingId;
        using (PlacementHarness first = new(_database))
        {
            owner = await first.CreatePlayerAsync();
            _ = await Place(first, owner);
            BuildingInfo coop = await first.BuildAsync(owner, BuildingKind.Coop);
            buildingId = coop.Id;
            await first.GiveCoinsAsync(owner, 800);
            hen = (await first.Buildings.BuyAnimalAsync(first.Presence.ConnectionOf(owner), coop.Id, AnimalKind.Chicken, "Bella")).Id;
        }

        using PlacementHarness second = new(_database);
        _ = await Place(second, owner);
        IReadOnlyList<BuildingView> views = await second.Buildings.ListAsync(second.Presence.ConnectionOf(owner), mine: true);

        views.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = buildingId, Kind = BuildingKind.Coop, Animals = 1, Capacity = 4 });
        BuildingInfo reloaded = (await second.BuildingDirectory.FindAsync(buildingId))!;
        (reloaded.Kind, reloaded.Land).Should().Be((BuildingKind.Coop, new MapInstance(GameMaps.Farm, owner)));
        (await second.Access.CanEnterAsync(reloaded.Interior, owner)).Should().BeFalse("a coop built on the other harness's clock is not finished on this one's");
        hen.Should().NotBe(Guid.Empty);
    }

    // Stands the player on their farm (the place they are in after joining), returns the id.
    private static Task<Guid> Place(PlacementHarness world, Guid player)
    {
        world.StandOnFarm(player, 64, 17);
        return Task.FromResult(player);
    }

    // The owner lets a player visit the farm (an allow-list row, docs/adr/ADR-019).
    private static async Task GrantVisitAsync(PlacementHarness world, Guid owner, Guid visitor)
    {
        await using NosumDbContext db = world.Db();
        db.VisitGrants.Add(new VisitGrant { OwnerId = owner, VisitorId = visitor, CreatedAt = PlacementHarness.Start });
        _ = await db.SaveChangesAsync();
    }
}
