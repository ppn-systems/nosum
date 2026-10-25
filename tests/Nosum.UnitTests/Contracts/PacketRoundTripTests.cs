// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The new packets survive serialize and deserialize with every field, including nested rows and lists.</summary>
public sealed class PacketRoundTripTests
{
    [Fact]
    public void InventorySnapshot_RoundTrips()
    {
        using InventorySnapshot packet = new() { Capacity = 36, SelectedSlot = 3 };
        packet.Slots.Add(new InventorySlotRow { Slot = 0, ItemId = 1, Quantity = 1 });
        packet.Slots.Add(new InventorySlotRow { Slot = 20, ItemId = 202, Quantity = 99 });

        InventorySnapshot copy = InventorySnapshot.Deserialize(packet.Serialize());

        copy.Capacity.Should().Be(36);
        copy.SelectedSlot.Should().Be(3);
        copy.Slots.Should().HaveCount(2);
        copy.Slots[1].Slot.Should().Be(20);
        copy.Slots[1].ItemId.Should().Be(202);
        copy.Slots[1].Quantity.Should().Be(99);
    }

    [Fact]
    public void MoveSlotAndUseItem_RoundTrip()
    {
        using MoveSlot sentMove = new() { From = 2, To = 11, Quantity = 7 };
        using UseItem sentUse = new() { Slot = 4, TileX = 61, TileY = 17 };
        MoveSlot move = MoveSlot.Deserialize(sentMove.Serialize());
        UseItem use = UseItem.Deserialize(sentUse.Serialize());

        (move.From, move.To, move.Quantity).Should().Be(((byte)2, (byte)11, 7));
        (use.Slot, use.TileX, use.TileY).Should().Be(((byte)4, 61, 17));
    }

    [Fact]
    public void FarmTilesChanged_RoundTrips()
    {
        using FarmTilesChanged packet = new() { MapId = "farm", ServerNowMs = 1_790_000_000_123 };
        packet.Tiles.Add(new FarmTileRow { X = 61, Y = 17, CropId = 3, PlantedAtMs = 5, GrowthMs = 6, SettledAtMs = 7, WetUntilMs = 8 });

        FarmTilesChanged copy = FarmTilesChanged.Deserialize(packet.Serialize());

        copy.MapId.Should().Be("farm");
        copy.ServerNowMs.Should().Be(1_790_000_000_123);
        copy.Tiles.Should().ContainSingle().Which.Should().BeEquivalentTo(new FarmTileRow { X = 61, Y = 17, CropId = 3, PlantedAtMs = 5, GrowthMs = 6, SettledAtMs = 7, WetUntilMs = 8 });
    }

    [Fact]
    public void UseItemResult_RoundTrips()
    {
        using UseItemResult sent = new() { Status = FarmStatus.Ok, Action = FarmAction.Harvested };
        UseItemResult copy = UseItemResult.Deserialize(sent.Serialize());

        copy.Status.Should().Be(FarmStatus.Ok);
        copy.Action.Should().Be(FarmAction.Harvested);
    }

    [Fact]
    public void QuestUpdate_WithNestedRowAndProgressList_RoundTrips()
    {
        using QuestUpdate packet = new();
        packet.Quest.QuestId = 1;
        packet.Quest.State = QuestState.Completed;
        packet.Quest.Progress.Add(1);
        packet.Quest.Progress.Add(2);

        QuestUpdate copy = QuestUpdate.Deserialize(packet.Serialize());

        copy.Quest.QuestId.Should().Be(1);
        copy.Quest.State.Should().Be(QuestState.Completed);
        copy.Quest.Progress.Should().Equal(1, 2);
    }

    [Fact]
    public void QuestLog_RoundTrips()
    {
        using QuestLog packet = new();
        packet.Quests.Add(new QuestRow { QuestId = 1, State = QuestState.Active, Progress = [0, 1] });

        QuestLog copy = QuestLog.Deserialize(packet.Serialize());

        copy.Quests.Should().ContainSingle().Which.Progress.Should().Equal(0, 1);
    }
}
