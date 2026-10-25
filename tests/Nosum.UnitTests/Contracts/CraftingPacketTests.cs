// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The crafting, chest and furnace packets (docs/adr/ADR-024 D6): round trips and the opcode slice of the Placeable band.</summary>
public sealed class CraftingPacketTests
{
    [Fact]
    public void Craft_AndItsResult_RoundTrip()
    {
        using Craft request = new() { RecipeId = 6, Times = 99 };
        using CraftResult result = new() { Status = CraftStatus.NoSpace, RecipeId = 6, Times = 0 };

        Craft requestCopy = Craft.Deserialize(request.Serialize());
        CraftResult resultCopy = CraftResult.Deserialize(result.Serialize());

        (requestCopy.RecipeId, requestCopy.Times).Should().Be(((ushort)6, (byte)99));
        (resultCopy.Status, resultCopy.RecipeId, resultCopy.Times).Should().Be((CraftStatus.NoSpace, (ushort)6, (byte)0));
    }

    [Fact]
    public void RecipeBook_RoundTrips_AndGetRecipeBookIsEmpty()
    {
        using RecipeBook book = new();
        book.Unlocked.AddRange([1, 2, 3, 9]);
        using GetRecipeBook get = new();

        RecipeBook copy = RecipeBook.Deserialize(book.Serialize());

        copy.Unlocked.Should().Equal(1, 2, 3, 9);
        _ = GetRecipeBook.Deserialize(get.Serialize());
    }

    [Fact]
    public void ChestSnapshot_RoundTrips()
    {
        using ChestSnapshot snapshot = new() { Status = ChestStatus.Ok, ObjectId = 4711, Capacity = 36 };
        snapshot.Slots.Add(new InventorySlotRow { Slot = 0, ItemId = 500, Quantity = 99 });
        snapshot.Slots.Add(new InventorySlotRow { Slot = 35, ItemId = 501, Quantity = 1 });

        ChestSnapshot copy = ChestSnapshot.Deserialize(snapshot.Serialize());

        (copy.Status, copy.ObjectId, copy.Capacity).Should().Be((ChestStatus.Ok, 4711, (byte)36));
        copy.Slots.Should().HaveCount(2);
        copy.Slots[1].Should().BeEquivalentTo(new InventorySlotRow { Slot = 35, ItemId = 501, Quantity = 1 });
    }

    [Fact]
    public void ChestMove_AndChestResult_RoundTrip()
    {
        using ChestMove move = new() { ObjectId = 12, FromSide = ChestSide.Player, FromSlot = 3, ToSide = ChestSide.Chest, ToSlot = byte.MaxValue, Quantity = 17 };
        using ChestResult result = new() { Status = ChestStatus.Blocked, ObjectId = 12 };

        ChestMove copy = ChestMove.Deserialize(move.Serialize());
        ChestResult resultCopy = ChestResult.Deserialize(result.Serialize());

        (copy.ObjectId, copy.FromSide, copy.FromSlot, copy.ToSide, copy.ToSlot, copy.Quantity).Should().Be((12, ChestSide.Player, (byte)3, ChestSide.Chest, byte.MaxValue, (ushort)17));
        (resultCopy.Status, resultCopy.ObjectId).Should().Be((ChestStatus.Blocked, 12));
    }

    [Fact]
    public void ChestUpdate_ChestClosed_OpenAndCloseChest_RoundTrip()
    {
        using ChestUpdate update = new() { ObjectId = 8 };
        update.Slots.Add(new InventorySlotRow { Slot = 4, ItemId = 0, Quantity = 0 });
        using ChestClosed closed = new() { ObjectId = 8 };
        using OpenChest open = new() { ObjectId = 8 };
        using CloseChest close = new() { ObjectId = 8 };

        ChestUpdate updateCopy = ChestUpdate.Deserialize(update.Serialize());

        updateCopy.ObjectId.Should().Be(8);
        updateCopy.Slots.Should().ContainSingle().Which.Slot.Should().Be(4);
        ChestClosed.Deserialize(closed.Serialize()).ObjectId.Should().Be(8);
        OpenChest.Deserialize(open.Serialize()).ObjectId.Should().Be(8);
        CloseChest.Deserialize(close.Serialize()).ObjectId.Should().Be(8);
    }

    [Fact]
    public void FurnacePackets_RoundTrip()
    {
        using StartSmelting start = new() { ObjectId = 9, OreItemId = 504 };
        using CollectFurnace collect = new() { ObjectId = 9 };
        using FurnaceResult result = new() { Status = FurnaceStatus.Ok, ObjectId = 9, ItemId = 506, Quantity = 1 };
        using FurnaceSnapshot snapshot = new();
        snapshot.Furnaces.Add(new FurnaceRow { ObjectId = 9, State = FurnaceStateCode.Working, OreItemId = 504, RemainingMs = 899_000 });
        using FurnaceChanged changed = new() { ObjectId = 9, State = FurnaceStateCode.Ready, OreItemId = 504, RemainingMs = 0 };

        StartSmelting startCopy = StartSmelting.Deserialize(start.Serialize());
        FurnaceResult resultCopy = FurnaceResult.Deserialize(result.Serialize());
        FurnaceSnapshot snapshotCopy = FurnaceSnapshot.Deserialize(snapshot.Serialize());
        FurnaceChanged changedCopy = FurnaceChanged.Deserialize(changed.Serialize());

        (startCopy.ObjectId, startCopy.OreItemId).Should().Be((9, 504));
        CollectFurnace.Deserialize(collect.Serialize()).ObjectId.Should().Be(9);
        (resultCopy.Status, resultCopy.ObjectId, resultCopy.ItemId, resultCopy.Quantity).Should().Be((FurnaceStatus.Ok, 9, 506, 1));
        snapshotCopy.Furnaces.Should().ContainSingle().Which.Should().BeEquivalentTo(new FurnaceRow { ObjectId = 9, State = FurnaceStateCode.Working, OreItemId = 504, RemainingMs = 899_000 });
        (changedCopy.ObjectId, changedCopy.State, changedCopy.OreItemId, changedCopy.RemainingMs).Should().Be((9, FurnaceStateCode.Ready, 504, 0));
        using GetFurnaces get = new();
        _ = GetFurnaces.Deserialize(get.Serialize());
    }

    [Fact]
    public void PlaceableOpcodes_AreUnique_InsideTheBand_AndTheNewOnesInTheirSlice()
    {
        FieldInfo[] fields = [.. typeof(PlaceableOpCode).GetFields(BindingFlags.Public | BindingFlags.Static)];
        ushort[] all = [.. fields.Select(f => (ushort)f.GetRawConstantValue()!)];

        all.Should().OnlyHaveUniqueItems().And.OnlyContain(o => o >= 0xB000 && o <= 0xB0FF + 0x100);

        string[] requests = [nameof(PlaceableOpCode.Craft), nameof(PlaceableOpCode.GetRecipeBook), nameof(PlaceableOpCode.OpenChest), nameof(PlaceableOpCode.CloseChest), nameof(PlaceableOpCode.ChestMove), nameof(PlaceableOpCode.StartSmelting), nameof(PlaceableOpCode.CollectFurnace), nameof(PlaceableOpCode.GetFurnaces)];
        string[] replies = [nameof(PlaceableOpCode.CraftResult), nameof(PlaceableOpCode.RecipeBook), nameof(PlaceableOpCode.ChestSnapshot), nameof(PlaceableOpCode.ChestResult), nameof(PlaceableOpCode.ChestUpdate), nameof(PlaceableOpCode.ChestClosed), nameof(PlaceableOpCode.FurnaceResult), nameof(PlaceableOpCode.FurnaceSnapshot), nameof(PlaceableOpCode.FurnaceChanged)];
        foreach (string name in requests)
        {
            ((ushort)typeof(PlaceableOpCode).GetField(name)!.GetRawConstantValue()!).Should().BeInRange((ushort)0xB100, (ushort)0xB13F, name);
        }

        foreach (string name in replies)
        {
            ((ushort)typeof(PlaceableOpCode).GetField(name)!.GetRawConstantValue()!).Should().BeInRange((ushort)0xB140, (ushort)0xB17F, name);
        }
    }

    [Fact]
    public void PacketClasses_CarryTheirOpcodeConstants()
    {
        Craft.StaticOpCode.Should().Be(PlaceableOpCode.Craft);
        GetRecipeBook.StaticOpCode.Should().Be(PlaceableOpCode.GetRecipeBook);
        OpenChest.StaticOpCode.Should().Be(PlaceableOpCode.OpenChest);
        CloseChest.StaticOpCode.Should().Be(PlaceableOpCode.CloseChest);
        ChestMove.StaticOpCode.Should().Be(PlaceableOpCode.ChestMove);
        StartSmelting.StaticOpCode.Should().Be(PlaceableOpCode.StartSmelting);
        CollectFurnace.StaticOpCode.Should().Be(PlaceableOpCode.CollectFurnace);
        GetFurnaces.StaticOpCode.Should().Be(PlaceableOpCode.GetFurnaces);
        CraftResult.StaticOpCode.Should().Be(PlaceableOpCode.CraftResult);
        RecipeBook.StaticOpCode.Should().Be(PlaceableOpCode.RecipeBook);
        ChestSnapshot.StaticOpCode.Should().Be(PlaceableOpCode.ChestSnapshot);
        ChestResult.StaticOpCode.Should().Be(PlaceableOpCode.ChestResult);
        ChestUpdate.StaticOpCode.Should().Be(PlaceableOpCode.ChestUpdate);
        ChestClosed.StaticOpCode.Should().Be(PlaceableOpCode.ChestClosed);
        FurnaceResult.StaticOpCode.Should().Be(PlaceableOpCode.FurnaceResult);
        FurnaceSnapshot.StaticOpCode.Should().Be(PlaceableOpCode.FurnaceSnapshot);
        FurnaceChanged.StaticOpCode.Should().Be(PlaceableOpCode.FurnaceChanged);
    }

    [Fact]
    public void StatusValues_AreAppendOnly()
    {
        ((byte)PlaceStatus.NotYourPlace).Should().Be(14);
        ((byte)PlaceStatus.Tilled).Should().Be(15);
        ((byte)PlaceStatus.NodeInTheWay).Should().Be(16);
        ((byte)PlaceStatus.NotEmpty).Should().Be(17);
        ((byte)PlaceStatus.InUse).Should().Be(18);
        Enum.GetValues<CraftStatus>().Select(v => (byte)v).Should().Equal(0, 1, 2, 3, 4, 5, 6);
        Enum.GetValues<ChestSide>().Select(v => (byte)v).Should().Equal(0, 1);
    }
}
