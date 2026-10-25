// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The pure rules behind the crafted objects: sprinklers, furnaces, who may open a chest, container ids (docs/adr/ADR-024 D4).</summary>
public sealed class PlacedBehaviourRulesTests
{
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(5, 4, true)]
    [InlineData(5, 6, true)]
    [InlineData(4, 5, true)]
    [InlineData(6, 5, true)]
    [InlineData(5, 5, false)]
    [InlineData(4, 4, false)]
    [InlineData(6, 6, false)]
    [InlineData(5, 7, false)]
    [InlineData(7, 5, false)]
    public void Waters_OnlyTheFourOrthogonalNeighbours(int x, int y, bool expected)
    {
        SprinklerRules.Waters(5, 5, x, y).Should().Be(expected);
    }

    [Fact]
    public void IsWatered_CountsOnlySprinklers_AndAnyOfThem()
    {
        PlacedItem[] placed =
        [
            new(1, ItemCatalog.Chest, 3, 3, 0),
            new(2, ItemCatalog.Sprinkler, 10, 10, 0),
            new(3, ItemCatalog.Sprinkler, 12, 10, 0),
        ];

        SprinklerRules.IsWatered(placed, 3, 4).Should().BeFalse("a chest does not water");
        SprinklerRules.IsWatered(placed, 11, 10).Should().BeTrue("both sprinklers cover the tile between them");
        SprinklerRules.IsWatered(placed, 10, 9).Should().BeTrue();
        SprinklerRules.IsWatered(placed, 10, 10).Should().BeFalse("the sprinkler's own tile is not watered");
        SprinklerRules.IsWatered([], 11, 10).Should().BeFalse();
    }

    [Fact]
    public void IsWatered_IgnoresUnknownItems()
    {
        SprinklerRules.IsWatered([new PlacedItem(1, 99_999, 5, 5, 0)], 5, 4).Should().BeFalse();
    }

    [Theory]
    [InlineData(ItemCatalog.CopperOre, ItemCatalog.CopperBar)]
    [InlineData(ItemCatalog.IronOre, ItemCatalog.IronBar)]
    public void TryBarOf_Ore_GivesItsBar(int ore, int bar)
    {
        FurnaceRules.TryBarOf(ore, out int found).Should().BeTrue();
        found.Should().Be(bar);
    }

    [Theory]
    [InlineData(ItemCatalog.Stone)]
    [InlineData(ItemCatalog.Coal)]
    [InlineData(ItemCatalog.CopperBar)]
    [InlineData(0)]
    public void TryBarOf_AnythingElse_IsNotSmeltable(int itemId)
    {
        FurnaceRules.TryBarOf(itemId, out _).Should().BeFalse();
    }

    [Fact]
    public void Furnace_ThirtyGameMinutes_AreFifteenRealMinutesAtTheDefaultDayLength()
    {
        FurnaceRules.ReadyAtMs(1_000, GameCalendar.Default).Should().Be(1_000 + (15 * 60 * 1000));
        (FurnaceRules.OreCost, FurnaceRules.CoalCost, FurnaceRules.GameMinutes).Should().Be((5, 1, 30));
    }

    [Fact]
    public void Furnace_FollowsTheCalendarsDayLength()
    {
        GameCalendar shortDays = new(0, TimeSpan.FromMinutes(24).Ticks / TimeSpan.TicksPerMillisecond);

        // A 24 real-minute day has one real second per game minute: thirty seconds.
        FurnaceRules.ReadyAtMs(0, shortDays).Should().Be(30_000);
    }

    [Fact]
    public void StateOf_IsWorkingUntilTheReadyInstant_ThenReady()
    {
        long started = 5_000_000;
        long ready = FurnaceRules.ReadyAtMs(started, GameCalendar.Default);

        FurnaceRules.StateOf(started, started, GameCalendar.Default).Should().Be(FurnaceState.Working);
        FurnaceRules.StateOf(started, ready - 1, GameCalendar.Default).Should().Be(FurnaceState.Working);
        FurnaceRules.StateOf(started, ready, GameCalendar.Default).Should().Be(FurnaceState.Ready);
        FurnaceRules.StateOf(started, ready + 86_400_000, GameCalendar.Default).Should().Be(FurnaceState.Ready, "a finished bar waits");
    }

    [Fact]
    public void MayUse_OwnerOfAPrivateFarm_Yes_VisitorAndSharedMap_No()
    {
        MapInstance farm = new(GameMaps.Farm, s_owner);

        PlacedAccess.MayUse(farm, s_owner, false).Should().BeTrue();
        PlacedAccess.MayUse(farm, s_other, false).Should().BeFalse("a visitor cannot open the owner's chest");
        PlacedAccess.MayUse(farm, s_other, true).Should().BeFalse("membership of some co-op does not open a private farm");
        PlacedAccess.MayUse(MapInstance.Shared(GameMaps.Town), s_owner, true).Should().BeFalse();
    }

    [Fact]
    public void MayUse_CoopLand_IsForItsMembersOnly()
    {
        Guid coop = Guid.Parse("33333333-3333-3333-3333-333333333333");
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, coop);

        PlacedAccess.MayUse(land, s_owner, true).Should().BeTrue();
        PlacedAccess.MayUse(land, s_owner, false).Should().BeFalse();
        PlacedAccess.MayUse(land, coop, false).Should().BeFalse("the co-op id is not a player");
    }

    [Fact]
    public void Reaches_AdjacentTile_IsWithinReach_AFarOneIsNot()
    {
        _ = PlaceableCatalog.TryGet(ItemCatalog.Chest, out PlaceableDefinition? chest);
        PlaceableView view = chest!.View(0);

        // Feet in the middle of the tile below the chest (tile 10, 11; 16 px tiles).
        PlacedAccess.Reaches(view, 10, 10, 168, 184, 16).Should().BeTrue();
        PlacedAccess.Reaches(view, 10, 10, 168, 300, 16).Should().BeFalse();
    }

    [Fact]
    public void ContainerIds_RoundTripTheObjectId_AndNeverLookLikeAnAccount()
    {
        Guid id = ContainerIds.ForChest(1_234_567);

        ContainerIds.IsContainer(id).Should().BeTrue();
        ContainerIds.TryGetChest(id, out int objectId).Should().BeTrue();
        objectId.Should().Be(1_234_567);
        ContainerIds.ForChest(1).Should().NotBe(ContainerIds.ForChest(2));
        ContainerIds.IsContainer(Guid.NewGuid()).Should().BeFalse();
        ContainerIds.IsContainer(Guid.CreateVersion7()).Should().BeFalse();
        ContainerIds.IsContainer(s_owner).Should().BeFalse();
        ContainerIds.IsContainer(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void Chest_HoldsThirtySixSlots_AndIsAPlaceableKind()
    {
        ChestRules.Capacity.Should().Be(36);
        _ = PlaceableCatalog.TryGet(ItemCatalog.Chest, out PlaceableDefinition? chest).Should().BeTrue();
        chest!.Kind.Should().Be(PlaceableKind.Chest);
        chest.BlocksWalking.Should().BeTrue();
    }

    [Fact]
    public void Kinds_AreAssignedToTheCraftedObjects()
    {
        Kind(ItemCatalog.WoodFence).Should().Be(PlaceableKind.Fence);
        Kind(ItemCatalog.StonePath).Should().Be(PlaceableKind.Path);
        Kind(ItemCatalog.Sprinkler).Should().Be(PlaceableKind.Sprinkler);
        Kind(ItemCatalog.Furnace).Should().Be(PlaceableKind.Furnace);
        Kind(ItemCatalog.Scarecrow).Should().Be(PlaceableKind.Decoration);
        Kind(ItemCatalog.OakChair).Should().Be(PlaceableKind.Decoration);
    }

    [Fact]
    public void PathTiles_DoNotBlockWalking_ButFencesAndChestsDo()
    {
        WalkGrid map = new(10, 10, 16, new bool[100]);
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.StonePath, 2, 2, 0));
        set.Add(new PlacedItem(2, ItemCatalog.WoodFence, 3, 2, 0));
        set.Add(new PlacedItem(3, ItemCatalog.Chest, 4, 2, 0));

        WalkGrid grid = set.BuildGrid(map);

        grid.IsBlocked(2, 2).Should().BeFalse("a path is walked over");
        grid.IsBlocked(3, 2).Should().BeTrue();
        grid.IsBlocked(4, 2).Should().BeTrue();
        map.IsBlocked(3, 2).Should().BeFalse("the map's own grid is not changed");
    }

    [Fact]
    public void Check_APathStillOccupiesItsTile()
    {
        WalkGrid map = new(10, 10, 16, new bool[100]);
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.StonePath, 2, 2, 0));
        _ = PlaceableCatalog.TryGet(ItemCatalog.WoodFence, out PlaceableDefinition? fence);

        set.Check(fence!, 0, 2, 2, map, new HashSet<(int X, int Y)>()).Should().Be(PlacementVerdict.Overlaps);
    }

    [Fact]
    public void Check_TilledSoilAndNodes_AreRefused_EverythingElseIsFree()
    {
        WalkGrid map = new(10, 10, 16, new bool[100]);
        _ = PlaceableCatalog.TryGet(ItemCatalog.WoodFence, out PlaceableDefinition? fence);
        FakeLayers layers = new(tilled: [(1, 1)], nodes: [(2, 2)]);
        PlacedSet set = new();

        set.Check(fence!, 0, 1, 1, map, new HashSet<(int X, int Y)>(), 0, layers).Should().Be(PlacementVerdict.Tilled);
        set.Check(fence!, 0, 2, 2, map, new HashSet<(int X, int Y)>(), 0, layers).Should().Be(PlacementVerdict.Node);
        set.Check(fence!, 0, 3, 3, map, new HashSet<(int X, int Y)>(), 0, layers).Should().Be(PlacementVerdict.Ok);
        set.Check(fence!, 0, 1, 1, map, new HashSet<(int X, int Y)>()).Should().Be(PlacementVerdict.Ok, "no layers, nothing to refuse");
    }

    [Fact]
    public void Check_ABlockedTileIsReportedBeforeTheLayers()
    {
        bool[] blocked = new bool[100];
        blocked[11] = true;
        WalkGrid map = new(10, 10, 16, blocked);
        _ = PlaceableCatalog.TryGet(ItemCatalog.WoodFence, out PlaceableDefinition? fence);

        new PlacedSet().Check(fence!, 0, 1, 1, map, new HashSet<(int X, int Y)>(), 0, new FakeLayers([(1, 1)], [])).Should().Be(PlacementVerdict.Blocked);
    }

    [Fact]
    public void BlockWhereBlocked_JoinsTwoLayersOfTheSameMap()
    {
        bool[] a = new bool[16];
        bool[] b = new bool[16];
        a[1] = true;
        b[2] = true;
        WalkGrid nodes = new(4, 4, 16, a);
        WalkGrid placed = new(4, 4, 16, b);

        WalkGrid joined = nodes.Clone();
        joined.BlockWhereBlocked(placed);

        joined.IsBlocked(1, 0).Should().BeTrue();
        joined.IsBlocked(2, 0).Should().BeTrue();
        joined.IsBlocked(3, 0).Should().BeFalse();
        nodes.IsBlocked(2, 0).Should().BeFalse("the source layers are not changed");
        placed.IsBlocked(1, 0).Should().BeFalse();
    }

    [Fact]
    public void BlockWhereBlocked_GridOfAnotherSize_Throws()
    {
        WalkGrid small = new(2, 2, 16, new bool[4]);
        WalkGrid big = new(3, 3, 16, new bool[9]);

        Action join = () => small.BlockWhereBlocked(big);

        join.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddToSlot_EmptySlot_TakesTheStack()
    {
        InventoryGrid grid = new(4);

        grid.AddToSlot(2, ItemCatalog.Wood, 10).Should().Be(InventoryVerdict.Ok);

        grid[2].Should().Be(new ItemSlot(ItemCatalog.Wood, 10));
        grid[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddToSlot_SameItem_MergesUpToTheLimit_AndRefusesTheWholeCallOtherwise()
    {
        InventoryGrid grid = new(2);
        _ = grid.AddToSlot(0, ItemCatalog.Wood, 90);

        grid.AddToSlot(0, ItemCatalog.Wood, 9).Should().Be(InventoryVerdict.Ok);
        grid.AddToSlot(0, ItemCatalog.Wood, 1).Should().Be(InventoryVerdict.NoSpace);

        grid[0].Quantity.Should().Be(99);
    }

    [Fact]
    public void AddToSlot_PartlyFitting_ChangesNothing()
    {
        InventoryGrid grid = new(1);
        _ = grid.AddToSlot(0, ItemCatalog.Wood, 95);
        List<int> changed = [];
        grid.TakeChanged(changed);
        changed.Clear();

        grid.AddToSlot(0, ItemCatalog.Wood, 10).Should().Be(InventoryVerdict.NoSpace);

        grid[0].Quantity.Should().Be(95);
        grid.TakeChanged(changed);
        changed.Should().BeEmpty();
    }

    [Fact]
    public void AddToSlot_OtherItemInTheSlot_IsBlocked()
    {
        InventoryGrid grid = new(2);
        _ = grid.AddToSlot(0, ItemCatalog.Wood, 1);

        grid.AddToSlot(0, ItemCatalog.Stone, 1).Should().Be(InventoryVerdict.Blocked);
    }

    [Theory]
    [InlineData(-1, 1, InventoryVerdict.InvalidSlot)]
    [InlineData(2, 1, InventoryVerdict.InvalidSlot)]
    [InlineData(0, 0, InventoryVerdict.InvalidQuantity)]
    [InlineData(0, -3, InventoryVerdict.InvalidQuantity)]
    public void AddToSlot_BadArguments_AreRefused(int slot, int quantity, InventoryVerdict expected)
    {
        new InventoryGrid(2).AddToSlot(slot, ItemCatalog.Wood, quantity).Should().Be(expected);
    }

    [Fact]
    public void AddToSlot_UnknownItem_IsRefused()
    {
        new InventoryGrid(2).AddToSlot(0, 987_654, 1).Should().Be(InventoryVerdict.UnknownItem);
    }

    private static PlaceableKind Kind(int itemId)
    {
        _ = PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? definition).Should().BeTrue();
        return definition!.Kind;
    }

    private sealed class FakeLayers : IGroundLayers
    {
        private readonly HashSet<(int X, int Y)> _tilled;
        private readonly HashSet<(int X, int Y)> _nodes;

        public FakeLayers((int X, int Y)[] tilled, (int X, int Y)[] nodes)
        {
            _tilled = [.. tilled];
            _nodes = [.. nodes];
        }

        public bool IsTilled(int x, int y) => _tilled.Contains((x, y));

        public bool HasNode(int x, int y) => _nodes.Contains((x, y));
    }
}
