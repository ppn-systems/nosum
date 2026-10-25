// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Farm buildings as data and rules: construction time, capacity, footprint and door (docs/adr/ADR-028 D1, D2).</summary>
public sealed class BuildingRulesTests
{
    private static readonly GameCalendar s_calendar = new(0, 12L * 60 * 60 * 1000);

    private static BuildingDefinition Coop()
    {
        _ = BuildingCatalog.TryGet(BuildingKind.Coop, out BuildingDefinition? definition).Should().BeTrue();
        return definition!;
    }

    // An open 30 x 30 field.
    private static WalkGrid Field() => new(30, 30, 16, new bool[900]);

    [Fact]
    public void CompletesAtMs_IsTwoGameDaysAfterPlacing()
    {
        BuildingRules.CompletesAtMs(1_000, s_calendar).Should().Be(1_000 + (2 * s_calendar.DayLengthMs));
    }

    [Fact]
    public void StateOf_BeforeAndAtCompletion_UnderConstructionThenFinished()
    {
        long completes = BuildingRules.CompletesAtMs(0, s_calendar);

        BuildingRules.StateOf(completes, completes - 1).Should().Be(BuildingState.UnderConstruction);
        BuildingRules.StateOf(completes, completes).Should().Be(BuildingState.Finished);
        BuildingRules.RemainingMs(completes, completes - 5_000).Should().Be(5_000);
        BuildingRules.RemainingMs(completes, completes + 1).Should().Be(0);
    }

    [Fact]
    public void CanHouse_FinishedBuildingWithRoom_IsOk()
    {
        BuildingRules.CanHouse(Coop(), AnimalKind.Chicken, BuildingState.Finished, 3).Should().Be(BuildingVerdict.Ok);
    }

    [Theory]
    [InlineData(BuildingState.UnderConstruction, AnimalKind.Chicken, 0, BuildingVerdict.NotFinished)]
    [InlineData(BuildingState.Finished, AnimalKind.Cow, 0, BuildingVerdict.WrongResident)]
    [InlineData(BuildingState.Finished, AnimalKind.Chicken, 4, BuildingVerdict.Full)]
    public void CanHouse_Refusals_NameTheReason(BuildingState state, AnimalKind species, int animals, BuildingVerdict expected)
    {
        BuildingRules.CanHouse(Coop(), species, state, animals).Should().Be(expected);
    }

    [Fact]
    public void Catalog_BuildingsCostWhatTheAdrSays_AndHoldFourAnimals()
    {
        BuildingDefinition coop = Coop();
        _ = BuildingCatalog.TryGet(BuildingKind.Barn, out BuildingDefinition? barn).Should().BeTrue();

        (coop.Coins, coop.Wood, coop.Stone, coop.Capacity).Should().Be((4_000L, 300, 100, 4));
        (barn!.Coins, barn.Wood, barn.Stone, barn.Capacity).Should().Be((6_000L, 350, 150, 4));
        BuildingCatalog.TryGetByBlueprint(ItemCatalog.BarnBlueprint, out BuildingDefinition? found).Should().BeTrue();
        found.Should().BeSameAs(barn);
        BuildingCatalog.TryGetByInterior("coop", out BuildingDefinition? byMap).Should().BeTrue();
        byMap.Should().BeSameAs(coop);
    }

    [Fact]
    public void Catalog_InteriorsArePrivateMapsOfTheirKind_AndTheDataFitsTheMapFiles()
    {
        foreach (BuildingDefinition building in BuildingCatalog.All)
        {
            GameMaps.TryGet(building.Key, out MapSpec? interior).Should().BeTrue();
            interior!.IsInterior.Should().BeTrue();
            interior.IsPrivate.Should().BeTrue();
            interior.InteriorOf.Should().Be(building.Kind);
            building.IsTrough(building.TroughLeft, building.TroughTop).Should().BeTrue();
            building.IsTrough(building.TroughLeft + building.TroughWidth, building.TroughTop).Should().BeFalse();
            building.InWanderArea(building.ArrivalX, building.ArrivalY).Should().BeFalse("players arrive at the door, not among the animals");
            building.View.FootprintHeight.Should().BeLessThan(building.View.Height / 16, "the roof overhangs the footprint");
        }
    }

    [Fact]
    public void InteriorOf_NamesTheInstanceByKindAndBuildingId()
    {
        Guid id = Guid.Parse("33333333-3333-3333-3333-333333333333");

        MapInstance interior = BuildingRules.InteriorOf(BuildingKind.Coop, id);

        interior.MapId.Should().Be("coop");
        interior.Key.Should().Be("coop@33333333333333333333333333333333");
        interior.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public void DoorTiles_AreInsideTheFootprint_AndInFrontOfIt()
    {
        BuildingDefinition coop = Coop();

        ((int X, int Y) door, (int X, int Y) front) = BuildingRules.DoorTiles(coop, 10, 8);

        door.Should().Be((11, 10), "the door is the second tile of the bottom row of a 6 x 3 footprint at (10, 8)");
        front.Should().Be((11, 11));
    }

    [Fact]
    public void Blueprints_ArePlaceableBuildingsWithADoor()
    {
        foreach (BuildingDefinition building in BuildingCatalog.All)
        {
            PlaceableCatalog.TryGet(building.BlueprintItemId, out PlaceableDefinition? placeable).Should().BeTrue();
            placeable!.Kind.Should().Be(PlaceableKind.Building);
            placeable.Door.Should().Be((building.DoorX, building.DoorY));
            ItemCatalog.TryGet(building.BlueprintItemId, out ItemDefinition? item).Should().BeTrue();
            item!.Kind.Should().Be(ItemKind.Placeable);
        }
    }

    [Fact]
    public void Check_BuildingOnOpenGround_IsOk()
    {
        PlaceableCatalog.TryGet(ItemCatalog.CoopBlueprint, out PlaceableDefinition? coop).Should().BeTrue();

        new PlacedSet().Check(coop!, 0, 5, 5, Field(), new HashSet<(int X, int Y)>()).Should().Be(PlacementVerdict.Ok);
    }

    [Fact]
    public void Check_BuildingWhoseDoorFrontIsOffTheMap_IsBlocked()
    {
        PlaceableCatalog.TryGet(ItemCatalog.CoopBlueprint, out PlaceableDefinition? coop).Should().BeTrue();

        // The footprint ends on row 29, the last row: there is no tile in front of the door.
        new PlacedSet().Check(coop!, 0, 5, 27, Field(), new HashSet<(int X, int Y)>()).Should().Be(PlacementVerdict.Blocked);
    }

    [Fact]
    public void Check_FenceInFrontOfADoor_IsRefused_ButAPathIsAllowed()
    {
        PlaceableCatalog.TryGet(ItemCatalog.CoopBlueprint, out PlaceableDefinition? coop).Should().BeTrue();
        PlaceableCatalog.TryGet(ItemCatalog.WoodFence, out PlaceableDefinition? fence).Should().BeTrue();
        PlaceableCatalog.TryGet(ItemCatalog.StonePath, out PlaceableDefinition? path).Should().BeTrue();
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.CoopBlueprint, 5, 5, 0));
        HashSet<(int X, int Y)> reserved = [];

        // The coop is at (5, 5): its door is (6, 7) and the tile in front of it (6, 8).
        set.Check(fence!, 0, 6, 8, Field(), reserved).Should().Be(PlacementVerdict.Overlaps);
        set.Check(fence!, 0, 7, 8, Field(), reserved).Should().Be(PlacementVerdict.Ok);
        set.Check(path!, 0, 6, 8, Field(), reserved).Should().Be(PlacementVerdict.Ok);
        _ = coop;
    }

    [Fact]
    public void Check_SecondBuildingOverTheFrontOfTheFirstsDoor_IsRefused()
    {
        PlaceableCatalog.TryGet(ItemCatalog.BarnBlueprint, out PlaceableDefinition? barn).Should().BeTrue();
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.CoopBlueprint, 5, 5, 0));

        set.Check(barn!, 0, 4, 8, Field(), new HashSet<(int X, int Y)>()).Should().Be(PlacementVerdict.Overlaps);
    }

    [Fact]
    public void Check_BuildingWhoseDoorFrontIsUnderAFence_IsBlocked()
    {
        PlaceableCatalog.TryGet(ItemCatalog.CoopBlueprint, out PlaceableDefinition? coop).Should().BeTrue();
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.WoodFence, 6, 8, 0));

        set.Check(coop!, 0, 5, 5, Field(), new HashSet<(int X, int Y)>()).Should().Be(PlacementVerdict.Blocked);
    }

    [Fact]
    public void BuildGrid_BlocksTheFootprintExceptTheDoorTile()
    {
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.CoopBlueprint, 5, 5, 0));

        WalkGrid grid = set.BuildGrid(Field());

        grid.IsBlocked(5, 5).Should().BeTrue();
        grid.IsBlocked(10, 7).Should().BeTrue();
        grid.IsBlocked(6, 7).Should().BeFalse("the door tile is how a player walks in");
        grid.IsBlocked(6, 8).Should().BeFalse();
        grid.IsBlocked(5, 7).Should().BeTrue();
    }
}
