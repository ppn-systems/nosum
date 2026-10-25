// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class InventoryGridTests
{
    private const int Seeds = ItemCatalog.ParsnipSeeds;
    private const int Other = ItemCatalog.RadishSeeds;

    [Fact]
    public void Add_IntoEmptyInventory_FillsTheFirstSlot()
    {
        InventoryGrid grid = new(4);

        InventoryVerdict verdict = grid.Add(Seeds, 5);

        verdict.Should().Be(InventoryVerdict.Ok);
        grid[0].Should().Be(new ItemSlot(Seeds, 5));
        grid.CountOf(Seeds).Should().Be(5);
    }

    [Fact]
    public void Add_ToExistingStack_TopsItUpBeforeOpeningANewSlot()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 90);

        _ = grid.Add(Seeds, 20);

        grid[0].Quantity.Should().Be(ItemCatalog.DefaultStack);
        grid[1].Should().Be(new ItemSlot(Seeds, 11));
    }

    [Fact]
    public void Add_WithoutRoom_RefusesAndChangesNothing()
    {
        InventoryGrid grid = new(1);
        _ = grid.Add(Seeds, 95);
        List<int> changed = [];
        grid.TakeChanged(changed);
        changed.Clear();

        InventoryVerdict verdict = grid.Add(Seeds, 10);

        verdict.Should().Be(InventoryVerdict.NoSpace);
        grid[0].Quantity.Should().Be(95);
        grid.TakeChanged(changed);
        changed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 5, InventoryVerdict.UnknownItem)]
    [InlineData(999, 1, InventoryVerdict.UnknownItem)]
    [InlineData(ItemCatalog.ParsnipSeeds, 0, InventoryVerdict.InvalidQuantity)]
    [InlineData(ItemCatalog.ParsnipSeeds, -3, InventoryVerdict.InvalidQuantity)]
    public void Add_InvalidItemOrQuantity_IsRefused(int itemId, int quantity, InventoryVerdict expected)
    {
        InventoryGrid grid = new(4);

        grid.Add(itemId, quantity).Should().Be(expected);
        grid.CountOf(itemId).Should().Be(0);
    }

    [Fact]
    public void Remove_AcrossSlots_TakesFromTheHighestSlotFirst()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 99);
        _ = grid.Add(Seeds, 10);

        InventoryVerdict verdict = grid.Remove(Seeds, 15);

        verdict.Should().Be(InventoryVerdict.Ok);
        grid[0].Quantity.Should().Be(94);
        grid[1].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Remove_MoreThanHeld_RefusesAndKeepsEverything()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 3);

        grid.Remove(Seeds, 4).Should().Be(InventoryVerdict.NotEnough);

        grid.CountOf(Seeds).Should().Be(3);
    }

    [Fact]
    public void RemoveFromSlot_WrongItem_IsRefused()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 3);

        grid.RemoveFromSlot(0, Other, 1).Should().Be(InventoryVerdict.EmptySlot);
        grid.RemoveFromSlot(0, Seeds, 4).Should().Be(InventoryVerdict.NotEnough);
        grid.RemoveFromSlot(9, Seeds, 1).Should().Be(InventoryVerdict.InvalidSlot);
        grid.RemoveFromSlot(0, Seeds, 1).Should().Be(InventoryVerdict.Ok);
        grid[0].Quantity.Should().Be(2);
    }

    [Fact]
    public void Move_WholeStackToEmptySlot_Moves()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 5);

        grid.Move(0, 3).Should().Be(InventoryVerdict.Ok);

        grid[0].IsEmpty.Should().BeTrue();
        grid[3].Should().Be(new ItemSlot(Seeds, 5));
    }

    [Fact]
    public void Move_PartOfStackToEmptySlot_SplitsIt()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 5);

        grid.Move(0, 2, 2).Should().Be(InventoryVerdict.Ok);

        grid[0].Quantity.Should().Be(3);
        grid[2].Quantity.Should().Be(2);
    }

    [Fact]
    public void Move_OntoSameItem_MergesUpToTheStackLimit()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 99);
        grid.Move(0, 1, 90).Should().Be(InventoryVerdict.Ok);
        grid[0].Quantity.Should().Be(9);
        grid[1].Quantity.Should().Be(90);

        grid.Move(0, 1).Should().Be(InventoryVerdict.Ok);

        grid[1].Quantity.Should().Be(99);
        grid[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Move_OntoFullStackOfSameItem_IsRefused()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 198);

        grid.Move(0, 1).Should().Be(InventoryVerdict.NoSpace);

        grid[0].Quantity.Should().Be(99);
    }

    [Fact]
    public void Move_OntoOtherItem_SwapsWholeStacks()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 5);
        _ = grid.Add(Other, 2);

        grid.Move(0, 1).Should().Be(InventoryVerdict.Ok);

        grid[0].Should().Be(new ItemSlot(Other, 2));
        grid[1].Should().Be(new ItemSlot(Seeds, 5));
    }

    [Fact]
    public void Move_PartOfStackOntoOtherItem_IsBlocked()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 5);
        _ = grid.Add(Other, 2);

        grid.Move(0, 1, 2).Should().Be(InventoryVerdict.Blocked);

        grid[0].Should().Be(new ItemSlot(Seeds, 5));
        grid[1].Should().Be(new ItemSlot(Other, 2));
    }

    [Theory]
    [InlineData(-1, 1, 0, InventoryVerdict.InvalidSlot)]
    [InlineData(0, 4, 0, InventoryVerdict.InvalidSlot)]
    [InlineData(0, 0, 0, InventoryVerdict.InvalidSlot)]
    [InlineData(2, 1, 0, InventoryVerdict.EmptySlot)]
    [InlineData(0, 1, 6, InventoryVerdict.InvalidQuantity)]
    public void Move_Invalid_IsRefusedAndChangesNothing(int from, int to, int quantity, InventoryVerdict expected)
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 5);

        grid.Move(from, to, quantity).Should().Be(expected);

        grid[0].Should().Be(new ItemSlot(Seeds, 5));
    }

    [Fact]
    public void TakeChanged_AfterOperations_ListsEachChangedSlotOnceAndClears()
    {
        InventoryGrid grid = new(4);
        _ = grid.Add(Seeds, 5);
        _ = grid.Move(0, 2);
        List<int> changed = [];

        grid.TakeChanged(changed);

        changed.Should().Equal(0, 2);
        List<int> again = [];
        grid.TakeChanged(again);
        again.Should().BeEmpty();
    }

    [Fact]
    public void StarterKit_FitsTheDefaultInventory_AndStartsWithTools()
    {
        InventoryGrid grid = new(InventoryLimits.DefaultCapacity);

        foreach (ItemSlot item in StarterKit.Items)
        {
            grid.Add(item.ItemId, item.Quantity).Should().Be(InventoryVerdict.Ok);
        }

        grid[0].ItemId.Should().Be(ItemCatalog.Hoe);
        grid[1].ItemId.Should().Be(ItemCatalog.WateringCan);
        grid.CountOf(ItemCatalog.ParsnipSeeds).Should().BeGreaterThan(0);
    }
}
