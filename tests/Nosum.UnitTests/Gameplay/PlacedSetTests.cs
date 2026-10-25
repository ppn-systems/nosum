// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The placement rules the server decides with and the client predicts with (docs/adr/ADR-015).</summary>
public sealed class PlacedSetTests
{
    private static readonly HashSet<(int X, int Y)> s_reserved = [(3, 9)];

    // A 10 x 10 room: walls around the border, one pillar at (5, 5).
    private static WalkGrid Room()
    {
        bool[] blocked = new bool[100];
        for (int i = 0; i < 10; i++)
        {
            blocked[i] = true;
            blocked[90 + i] = true;
            blocked[i * 10] = true;
            blocked[(i * 10) + 9] = true;
        }

        blocked[55] = true;
        return new WalkGrid(10, 10, 16, blocked);
    }

    private static PlaceableDefinition Definition(int itemId)
    {
        _ = PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? definition).Should().BeTrue();
        return definition!;
    }

    [Fact]
    public void Check_FreeFloor_IsOk()
    {
        new PlacedSet().Check(Definition(ItemCatalog.OakChair), 0, 2, 2, Room(), s_reserved).Should().Be(PlacementVerdict.Ok);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(9, 4)]
    public void Check_OnAWall_IsBlocked(int x, int y)
    {
        new PlacedSet().Check(Definition(ItemCatalog.OakChair), 0, x, y, Room(), s_reserved).Should().Be(PlacementVerdict.Blocked);
    }

    [Fact]
    public void Check_WideFootprintTouchingTheWall_IsBlocked_AndOutsideTheMapIsOutside()
    {
        PlacedSet set = new();
        PlaceableDefinition couch = Definition(ItemCatalog.BlueCouch);

        set.Check(couch, 0, 7, 3, Room(), s_reserved).Should().Be(PlacementVerdict.Blocked, "the third tile is the wall at x = 9");
        set.Check(couch, 0, 8, 3, Room(), s_reserved).Should().Be(PlacementVerdict.Blocked, "the room's right edge: the wall tile is x = 9 and 10 is outside");
        set.Check(couch, 0, -1, 3, Room(), s_reserved).Should().Be(PlacementVerdict.OutsideRoom);
        set.Check(couch, 0, 3, 10, Room(), s_reserved).Should().Be(PlacementVerdict.OutsideRoom);
    }

    [Fact]
    public void Check_ReservedTile_IsRefused()
    {
        new PlacedSet().Check(Definition(ItemCatalog.OakChair), 0, 3, 9 - 1, Room(), new HashSet<(int X, int Y)> { (3, 8) }).Should().Be(PlacementVerdict.Reserved);
    }

    [Fact]
    public void Check_OnTopOfAnotherPiece_Overlaps_ButNextToItIsFine()
    {
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.OakTable, 2, 2, 0));
        PlaceableDefinition chair = Definition(ItemCatalog.OakChair);

        set.Check(chair, 0, 3, 3, Room(), s_reserved).Should().Be(PlacementVerdict.Overlaps, "the table covers (2..3, 2..3)");
        set.Check(chair, 0, 4, 2, Room(), s_reserved).Should().Be(PlacementVerdict.Ok);
        set.Check(chair, 0, 2, 4, Room(), s_reserved).Should().Be(PlacementVerdict.Ok);
    }

    [Fact]
    public void Check_MovingAPieceOntoItself_IgnoresThePieceBeingMoved()
    {
        PlacedSet set = new();
        set.Add(new PlacedItem(7, ItemCatalog.OakTable, 2, 2, 0));
        PlaceableDefinition table = Definition(ItemCatalog.OakTable);

        set.Check(table, 0, 3, 2, Room(), s_reserved).Should().Be(PlacementVerdict.Overlaps);
        set.Check(table, 0, 3, 2, Room(), s_reserved, ignoreId: 7).Should().Be(PlacementVerdict.Ok);
    }

    [Theory]
    [InlineData(ItemCatalog.OakTable, 1)]
    [InlineData(ItemCatalog.OakChair, 4)]
    [InlineData(ItemCatalog.OakChair, 255)]
    public void Check_RotationThePieceDoesNotHave_IsRefused(int itemId, int rotation)
    {
        new PlacedSet().Check(Definition(itemId), rotation, 2, 2, Room(), s_reserved).Should().Be(PlacementVerdict.BadRotation);
    }

    [Fact]
    public void Check_RotationChangesTheFootprint()
    {
        PlacedSet set = new();
        PlaceableDefinition couch = Definition(ItemCatalog.BlueCouch);

        // Facing down the couch is 3 x 1; seen from the side it is 2 x 2.
        couch.View(0).Should().Match<PlaceableView>(v => v.FootprintWidth == 3 && v.FootprintHeight == 1);
        couch.View(1).Should().Match<PlaceableView>(v => v.FootprintWidth == 2 && v.FootprintHeight == 2);
        set.Check(couch, 1, 1, 8, Room(), s_reserved).Should().Be(PlacementVerdict.Blocked, "the bottom row of the side view is the wall");
        set.Check(couch, 1, 1, 7, Room(), s_reserved).Should().Be(PlacementVerdict.Ok);
    }

    [Fact]
    public void Check_PillarInTheRoom_IsPartOfTheMapAndBlocks()
    {
        new PlacedSet().Check(Definition(ItemCatalog.OakTable), 0, 4, 4, Room(), s_reserved).Should().Be(PlacementVerdict.Blocked);
    }

    [Fact]
    public void BuildGrid_BlocksTheFootprints_AndLeavesTheMapsOwnGridAlone()
    {
        WalkGrid map = Room();
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.OakTable, 2, 2, 0));

        WalkGrid walkable = set.BuildGrid(map);

        walkable.IsBlocked(2, 2).Should().BeTrue();
        walkable.IsBlocked(3, 3).Should().BeTrue();
        walkable.IsBlocked(4, 2).Should().BeFalse();
        map.IsBlocked(2, 2).Should().BeFalse("the shared grid of the map is never changed");
    }

    [Fact]
    public void TryFindAt_ReturnsThePieceCoveringTheTile()
    {
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.OakTable, 2, 2, 0));
        set.Add(new PlacedItem(2, ItemCatalog.OakChair, 6, 6, 0));

        set.TryFindAt(3, 3, out PlacedItem table).Should().BeTrue();
        table.Id.Should().Be(1);
        set.TryFindAt(6, 6, out PlacedItem chair).Should().BeTrue();
        chair.Id.Should().Be(2);
        set.TryFindAt(4, 4, out _).Should().BeFalse();
    }

    [Fact]
    public void AddReplaceRemove_KeepTheSetInStep()
    {
        PlacedSet set = new();
        set.Add(new PlacedItem(1, ItemCatalog.OakChair, 2, 2, 0));

        set.Replace(new PlacedItem(1, ItemCatalog.OakChair, 4, 4, 1)).Should().BeTrue();
        set.Replace(new PlacedItem(9, ItemCatalog.OakChair, 4, 4, 1)).Should().BeFalse();
        set.TryGet(1, out PlacedItem chair).Should().BeTrue();
        (chair.TileX, chair.Rotation).Should().Be((4, 1));
        set.Remove(1).Should().BeTrue();
        set.Remove(1).Should().BeFalse();
        set.Count.Should().Be(0);
    }

    [Fact]
    public void Catalogue_EveryFurnitureItemIsPlaceable_AndEveryPlaceableIsAnItem()
    {
        IEnumerable<int> furnitureItems = ItemCatalog.All.Where(i => i.Kind == ItemKind.Furniture).Select(i => i.Id);

        PlaceableCatalog.All.Select(p => p.ItemId).Should().BeEquivalentTo(furnitureItems);
        foreach (PlaceableDefinition definition in PlaceableCatalog.All)
        {
            definition.Rotations.Should().BeOneOf(1, 4);
            for (int rotation = 0; rotation < definition.Rotations; rotation++)
            {
                PlaceableView view = definition.View(rotation);
                view.Width.Should().BeGreaterThan(0);
                view.Height.Should().BeGreaterThanOrEqualTo(view.FootprintHeight * 16, "the sprite covers its footprint");
            }
        }
    }

    [Fact]
    public void StarterKit_HasNoFurnitureOrClothing_AndFitsTheInventory()
    {
        // Furniture and clothing are bought at the shop (docs/adr/ADR-013 amendments); a new player starts with tools and seeds only.
        StarterKit.Items.Count(i => PlaceableCatalog.TryGet(i.ItemId, out _)).Should().Be(0);
        StarterKit.Items.Select(i => ItemCatalog.TryGet(i.ItemId, out ItemDefinition? d) ? d.Kind : ItemKind.Tool)
            .Should().OnlyContain(k => k == ItemKind.Tool || k == ItemKind.Seed);
        InventoryGrid grid = new(InventoryLimits.DefaultCapacity);
        foreach (ItemSlot item in StarterKit.Items)
        {
            grid.Add(item.ItemId, item.Quantity).Should().Be(InventoryVerdict.Ok);
        }
    }

    [Theory]
    [InlineData(40, 63, true)]
    [InlineData(40, 80, false)]
    [InlineData(76, 63, false)]
    [InlineData(47.9, 63.9, true)]
    public void CoversFeet_TileFootprintAndFeetBox_OverlapOnlyWhenTheyIntersect(double feetX, double feetY, bool covers)
    {
        // A 1 x 1 piece on tile (2, 3): pixels x 32..48, y 48..64.
        PlaceableView view = Definition(ItemCatalog.OakChair).View(0);

        PlacedSet.CoversFeet(view, 2, 3, feetX, feetY, 16).Should().Be(covers);
    }
}
