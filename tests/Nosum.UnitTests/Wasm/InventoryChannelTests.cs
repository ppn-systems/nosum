// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class InventoryChannelTests
{
    private static InventorySnapshot Snapshot()
    {
        InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 1 };
        snapshot.Slots.Add(new InventorySlotRow { Slot = 0, ItemId = ItemCatalog.Hoe, Quantity = 1 });
        snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.ParsnipSeeds, Quantity = 8 });
        return snapshot;
    }

    [Fact]
    public void Load_AppliesTheSnapshotAndRaisesChanged()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        using InventoryChannel channel = new(socket);
        int changed = 0;
        channel.Changed += () => changed++;

        channel.Load();

        channel.Capacity.Should().Be(36);
        channel.SelectedSlot.Should().Be(1);
        channel.SlotAt(2).Should().Be(new SlotInfo(ItemCatalog.ParsnipSeeds, 8));
        channel.SlotAt(1).Should().Be(default(SlotInfo));
        changed.Should().Be(1);
    }

    [Fact]
    public void PushedUpdate_ChangesOnlyTheNamedSlots_AndEmptiesWhenItemIdIsZero()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        using InventoryChannel channel = new(socket);
        channel.Load();
        InventoryUpdate update = new();
        update.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.ParsnipSeeds, Quantity = 7 });
        update.Slots.Add(new InventorySlotRow { Slot = 0, ItemId = 0, Quantity = 0 });
        update.Slots.Add(new InventorySlotRow { Slot = 5, ItemId = ItemCatalog.Parsnip, Quantity = 1 });

        socket.Push(update);

        channel.SlotAt(2).Quantity.Should().Be(7);
        channel.SlotAt(0).ItemId.Should().Be(0);
        channel.SlotAt(5).Should().Be(new SlotInfo(ItemCatalog.Parsnip, 1));
        update.Dispose();
    }

    [Fact]
    public void Select_ShowsAtOnceAndAsksTheServer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok, SelectedSlot = 4 });
        using InventoryChannel channel = new(socket);
        channel.Load();

        channel.Select(4);

        channel.SelectedSlot.Should().Be(4);
        socket.Requests.Should().Contain(r => r is SelectSlot);
    }

    [Fact]
    public void Select_WhenTheServerRefuses_GoesBackToTheSlotItKeeps()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.InvalidSlot, SelectedSlot = 1 });
        using InventoryChannel channel = new(socket);
        channel.Load();
        List<InventoryStatus> refused = [];
        channel.Refused += refused.Add;

        channel.Select(7);

        channel.SelectedSlot.Should().Be(1);
        refused.Should().Equal(InventoryStatus.InvalidSlot);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(-1)]
    [InlineData(35)]
    public void Select_OutsideTheHotbar_IsIgnoredLocally(int slot)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        using InventoryChannel channel = new(socket);
        channel.Load();

        channel.Select(slot);

        channel.SelectedSlot.Should().Be(1);
        socket.Requests.Should().NotContain(r => r is SelectSlot);
    }

    [Fact]
    public void Move_SendsTheRequestAndLeavesTheSlotsToTheServersUpdate()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok });
        using InventoryChannel channel = new(socket);
        channel.Load();

        channel.Move(2, 10, 3);

        MoveSlot sent = socket.Requests.Should().ContainSingle(r => r is MoveSlot).Subject.Should().BeOfType<MoveSlot>().Subject;
        (sent.From, sent.To, sent.Quantity).Should().Be(((byte)2, (byte)10, 3));
        channel.SlotAt(2).Quantity.Should().Be(8);
    }

    [Fact]
    public void Sort_SendsTheRequestAndLeavesTheSlotsToTheServersUpdate()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok });
        using InventoryChannel channel = new(socket);
        channel.Load();

        channel.Sort(lockHotbar: true);

        socket.Requests.Should().ContainSingle(r => r is SortInventory);
        channel.SlotAt(2).Quantity.Should().Be(8);
    }

    [Fact]
    public void Sort_SendsTheHotbarLockChoice()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok });
        using InventoryChannel channel = new(socket);
        channel.Load();

        channel.Sort(lockHotbar: false);

        socket.Requests.OfType<SortInventory>().Single().LockHotbar.Should().BeFalse();
    }

    [Fact]
    public void Sort_WhenTheServerRefuses_RaisesRefused()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.NoSpace });
        using InventoryChannel channel = new(socket);
        channel.Load();
        InventoryStatus? refused = null;
        channel.Refused += status => refused = status;

        channel.Sort(lockHotbar: true);

        refused.Should().Be(InventoryStatus.NoSpace);
    }
}
