// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Chest;
using Nosum.Wasm.ViewModels.Inventory;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class ChestViewModelTests
{
    private static (ChestViewModel Vm, FakeGameSocket Socket, ChestChannel Chest) Create(bool open = true)
    {
        FakeGameSocket socket = new();
        InventoryChannel inventory = LoadedInventory(socket, (0, ItemCatalog.Hoe, 1), (12, ItemCatalog.Wood, 30));
        InventoryViewModel bag = new(inventory, new FixedIconSource(), Localizer<Nosum.Wasm.ViewModels.Resources.Catalogue>(), Text());
        ChestChannel chest = new(socket);
        ChestViewModel vm = new(chest, inventory, bag, Describer(), Text());
        if (open)
        {
            socket.ReplyWith(_ =>
            {
                ChestSnapshot snapshot = new() { Status = ChestStatus.Ok, ObjectId = 7, Capacity = 36 };
                snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.Stone, Quantity = 4 });
                return snapshot;
            });
            chest.Open(7);
            socket.ReplyWith(_ => new ChestResult { Status = ChestStatus.Ok, ObjectId = 7 });
            socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok });
        }

        return (vm, socket, chest);
    }

    private static IReadOnlyList<ChestMove> Moves(FakeGameSocket socket) => [.. socket.Requests.OfType<ChestMove>()];

    [Fact]
    public void Open_WhenTheServerSaysOk_RaisesOpenChanged_AndShowsTheChestSlots()
    {
        FakeGameSocket socket = new();
        InventoryChannel inventory = LoadedInventory(socket);
        InventoryViewModel bag = new(inventory, new FixedIconSource(), Localizer<Nosum.Wasm.ViewModels.Resources.Catalogue>(), Text());
        ChestChannel chest = new(socket);
        ChestViewModel vm = new(chest, inventory, bag, Describer(), Text());
        List<bool> changes = [];
        vm.OpenChanged += changes.Add;
        socket.ReplyWith(_ =>
        {
            ChestSnapshot snapshot = new() { Status = ChestStatus.Ok, ObjectId = 7, Capacity = 36 };
            snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.Stone, Quantity = 4 });
            return snapshot;
        });

        chest.Open(7);

        changes.Should().Equal(true);
        vm.IsOpen.Should().BeTrue();
        vm.ChestSlots.Should().HaveCount(36);
        vm.ChestSlots[2].ItemId.Should().Be(ItemCatalog.Stone);
        vm.ChestSlots[2].Quantity.Should().Be(4);
    }

    [Fact]
    public void Open_Refused_ShowsNothing_AndRaisesNoOpenChanged()
    {
        FakeGameSocket socket = new();
        InventoryChannel inventory = LoadedInventory(socket);
        InventoryViewModel bag = new(inventory, new FixedIconSource(), Localizer<Nosum.Wasm.ViewModels.Resources.Catalogue>(), Text());
        ChestChannel chest = new(socket);
        ChestViewModel vm = new(chest, inventory, bag, Describer(), Text());
        List<bool> changes = [];
        vm.OpenChanged += changes.Add;
        socket.ReplyWith(_ => new ChestSnapshot { Status = ChestStatus.NotYours, ObjectId = 7 });

        chest.Open(7);

        changes.Should().BeEmpty();
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Click_PickThenPutOnTheOtherSide_AsksTheServerToMoveTheWholeStackToThatSlot()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.Click(ChestSide.Player, 12);
        vm.PickedSlot.Should().Be(12);
        vm.Click(ChestSide.Chest, 9);

        ChestMove move = Moves(socket).Single();
        (move.ObjectId, move.FromSide, move.FromSlot, move.ToSide, move.ToSlot, move.Quantity).Should().Be((7, ChestSide.Player, (byte)12, ChestSide.Chest, (byte)9, (ushort)0));
        vm.PickedSlot.Should().Be(-1);
    }

    [Fact]
    public void Click_AnEmptySlotFirst_PicksNothing()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.Click(ChestSide.Chest, 30);

        vm.PickedSlot.Should().Be(-1);
        Moves(socket).Should().BeEmpty();
    }

    [Fact]
    public void Click_TheSameSlotTwice_PutsTheStackDownWithoutAMove()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.Click(ChestSide.Chest, 2);
        vm.Click(ChestSide.Chest, 2);

        vm.PickedSlot.Should().Be(-1);
        Moves(socket).Should().BeEmpty();
    }

    [Fact]
    public void Click_InsideTheChest_MovesWithinTheChest()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.Click(ChestSide.Chest, 2);
        vm.Click(ChestSide.Chest, 20);

        ChestMove move = Moves(socket).Single();
        (move.FromSide, move.ToSide, move.FromSlot, move.ToSlot).Should().Be((ChestSide.Chest, ChestSide.Chest, (byte)2, (byte)20));
    }

    [Fact]
    public void Click_InsideTheBag_UsesTheInventoryMoveNotTheChest()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.Click(ChestSide.Player, 12);
        vm.Click(ChestSide.Player, 15);

        Moves(socket).Should().BeEmpty();
        MoveSlot move = socket.Requests.OfType<MoveSlot>().Single();
        (move.From, move.To).Should().Be(((byte)12, (byte)15));
    }

    [Fact]
    public void QuickMove_FromTheBag_SendsTheStackToAnySlotOfTheChest()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.QuickMove(ChestSide.Player, 12);

        ChestMove move = Moves(socket).Single();
        (move.FromSide, move.FromSlot, move.ToSide, move.ToSlot).Should().Be((ChestSide.Player, (byte)12, ChestSide.Chest, (byte)255));
    }

    [Fact]
    public void QuickMove_FromTheChest_SendsTheStackToAnySlotOfTheBag()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.QuickMove(ChestSide.Chest, 2);

        ChestMove move = Moves(socket).Single();
        (move.FromSide, move.FromSlot, move.ToSide, move.ToSlot).Should().Be((ChestSide.Chest, (byte)2, ChestSide.Player, (byte)255));
    }

    [Fact]
    public void QuickMove_AnEmptySlot_SendsNothing()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();

        vm.QuickMove(ChestSide.Chest, 30);

        Moves(socket).Should().BeEmpty();
    }

    [Fact]
    public void ARefusedMove_ShowsItsTextInTheStatusLine()
    {
        using CultureScope scope = new("en");
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();
        socket.ReplyWith(_ => new ChestResult { Status = ChestStatus.NoSpace, ObjectId = 7 });

        vm.QuickMove(ChestSide.Player, 12);

        vm.Status.Should().Be("There is no room.");
    }

    [Fact]
    public void ChestClosedByTheServer_RaisesOpenChangedFalse()
    {
        (ChestViewModel vm, FakeGameSocket socket, _) = Create();
        List<bool> changes = [];
        vm.OpenChanged += changes.Add;

        socket.Push(new ChestClosed { ObjectId = 7 });

        changes.Should().Equal(false);
        vm.IsOpen.Should().BeFalse();
    }

    private sealed class FixedIconSource : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = Icon;
            return true;
        }
    }
}
