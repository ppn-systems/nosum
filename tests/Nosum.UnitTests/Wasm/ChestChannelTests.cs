// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Inventory;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class ChestChannelTests
{
    private static InventorySlotRow Row(int slot, int item, int quantity) => new() { Slot = (byte)slot, ItemId = item, Quantity = quantity };

    private static ChestChannel Opened(FakeGameSocket socket, int objectId = 7)
    {
        socket.ReplyWith(_ =>
        {
            ChestSnapshot snapshot = new() { Status = ChestStatus.Ok, ObjectId = objectId, Capacity = 36 };
            snapshot.Slots.Add(Row(0, ItemCatalog.Wood, 40));
            snapshot.Slots.Add(Row(5, ItemCatalog.Stone, 3));
            return snapshot;
        });
        ChestChannel channel = new(socket);
        channel.Open(objectId);
        return channel;
    }

    [Fact]
    public void Capacity_IsTheServersChestSize()
    {
        ChestChannel.Capacity.Should().Be(ChestRules.Capacity);
    }

    [Fact]
    public void Open_WhenTheServerSaysOk_MirrorsTheSlots()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);

        channel.IsOpen.Should().BeTrue();
        channel.ObjectId.Should().Be(7);
        channel.SlotAt(0).Should().Be(new SlotInfo(ItemCatalog.Wood, 40));
        channel.SlotAt(5).Should().Be(new SlotInfo(ItemCatalog.Stone, 3));
        channel.SlotAt(1).Should().Be(default(SlotInfo));
        channel.Dispose();
    }

    [Fact]
    public void Open_WhenTheServerRefuses_OpensNothing_AndReportsWhy()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new ChestSnapshot { Status = ChestStatus.NotYours, ObjectId = 7 });
        using ChestChannel channel = new(socket);
        List<ChestStatus> answers = [];
        channel.Completed += answers.Add;

        channel.Open(7);

        channel.IsOpen.Should().BeFalse();
        answers.Should().Equal(ChestStatus.NotYours);
    }

    [Fact]
    public void Update_ReplacesSlotsOfTheOpenChest_AndRaisesChanged()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        int changed = 0;
        channel.Changed += () => changed++;
        ChestUpdate push = new() { ObjectId = 7 };
        push.Slots.Add(Row(0, 0, 0));
        push.Slots.Add(Row(1, ItemCatalog.Fiber, 9));

        socket.Push(push);

        channel.SlotAt(0).Should().Be(default(SlotInfo));
        channel.SlotAt(1).Should().Be(new SlotInfo(ItemCatalog.Fiber, 9));
        channel.SlotAt(5).Should().Be(new SlotInfo(ItemCatalog.Stone, 3));
        changed.Should().Be(1);
        push.Dispose();
        channel.Dispose();
    }

    [Fact]
    public void Update_ForAnotherChest_IsIgnored()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        ChestUpdate push = new() { ObjectId = 99 };
        push.Slots.Add(Row(0, ItemCatalog.Fiber, 1));

        socket.Push(push);

        channel.SlotAt(0).Should().Be(new SlotInfo(ItemCatalog.Wood, 40));
        push.Dispose();
        channel.Dispose();
    }

    [Fact]
    public void ChestClosedPush_ClosesTheMirror_AndRaisesClosed()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        int closed = 0;
        channel.Closed += () => closed++;

        socket.Push(new ChestClosed { ObjectId = 7 });

        channel.IsOpen.Should().BeFalse();
        channel.SlotAt(0).Should().Be(default(SlotInfo));
        closed.Should().Be(1);
        channel.Dispose();
    }

    [Fact]
    public void Move_SendsTheSidesSlotsAndQuantity()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        socket.ReplyWith(_ => new ChestResult { Status = ChestStatus.Ok, ObjectId = 7 });

        channel.Move(ChestSide.Player, 3, ChestSide.Chest, 12, 5);

        ChestMove request = (ChestMove)socket.Requests[^1];
        (request.ObjectId, request.FromSide, request.FromSlot, request.ToSide, request.ToSlot, request.Quantity)
            .Should().Be((7, ChestSide.Player, (byte)3, ChestSide.Chest, (byte)12, (ushort)5));
        channel.Dispose();
    }

    [Fact]
    public void Move_ToAnySlot_SendsSlot255()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        socket.ReplyWith(_ => new ChestResult { Status = ChestStatus.Ok, ObjectId = 7 });

        channel.Move(ChestSide.Chest, 0, ChestSide.Player, ChestChannel.AnySlot);

        ChestMove request = (ChestMove)socket.Requests[^1];
        request.ToSlot.Should().Be(255);
        request.Quantity.Should().Be(0, "zero moves the whole stack");
        channel.Dispose();
    }

    [Fact]
    public void Move_WithNothingOpen_SendsNothing()
    {
        FakeGameSocket socket = new();
        using ChestChannel channel = new(socket);

        channel.Move(ChestSide.Player, 0, ChestSide.Chest, 0);

        socket.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Move_RefusedAsOutOfReach_ClosesTheChest()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        socket.ReplyWith(_ => new ChestResult { Status = ChestStatus.OutOfReach, ObjectId = 7 });
        int closed = 0;
        channel.Closed += () => closed++;

        channel.Move(ChestSide.Player, 0, ChestSide.Chest, ChestChannel.AnySlot);

        channel.IsOpen.Should().BeFalse();
        closed.Should().Be(1);
        channel.Dispose();
    }

    [Fact]
    public void Close_ForgetsTheChest_AndRaisesClosedOnce()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        int closed = 0;
        channel.Closed += () => closed++;

        channel.Close();
        channel.Close();

        channel.IsOpen.Should().BeFalse();
        closed.Should().Be(1);
        channel.Dispose();
    }

    [Fact]
    public void Sort_SendsTheOpenChestId()
    {
        FakeGameSocket socket = new();
        ChestChannel channel = Opened(socket);
        socket.ReplyWith(_ => new ChestResult { Status = ChestStatus.Ok, ObjectId = 7 });

        channel.Sort();

        ChestSort request = (ChestSort)socket.Requests[^1];
        request.ObjectId.Should().Be(7);
        channel.Dispose();
    }

    [Fact]
    public void Sort_WithNothingOpen_SendsNothing()
    {
        FakeGameSocket socket = new();
        using ChestChannel channel = new(socket);

        channel.Sort();

        socket.Requests.Should().BeEmpty();
    }
}
