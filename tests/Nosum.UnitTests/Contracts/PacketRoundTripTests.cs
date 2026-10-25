// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
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
        packet.Tiles.Add(new FarmTileRow { X = 61, Y = 17, CropId = 3, PlantedAtMs = 5, GrowthMs = 6, SettledAtMs = 7, WetUntilMs = 8, GrowthDays = 9, WateredDay = 10, Removed = true });

        FarmTilesChanged copy = FarmTilesChanged.Deserialize(packet.Serialize());

        copy.MapId.Should().Be("farm");
        copy.ServerNowMs.Should().Be(1_790_000_000_123);
        copy.Tiles.Should().ContainSingle().Which.Should().BeEquivalentTo(new FarmTileRow { X = 61, Y = 17, CropId = 3, PlantedAtMs = 5, GrowthMs = 6, SettledAtMs = 7, WetUntilMs = 8, GrowthDays = 9, WateredDay = 10, Removed = true });
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

    [Fact]
    public void ListingsPage_WithGuidsAndPrices_RoundTrips()
    {
        Guid id = Guid.CreateVersion7();
        using ListingsPage packet = new() { ServerNowMs = 123, Page = 2, Total = 41 };
        packet.Rows.Add(new ListingRow { ListingId = id, ItemId = 202, Quantity = 9, Price = 1_000_000_000_000, ExpiresAtMs = 99, IsMine = true });

        ListingsPage copy = ListingsPage.Deserialize(packet.Serialize());

        copy.Total.Should().Be(41);
        copy.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new ListingRow { ListingId = id, ItemId = 202, Quantity = 9, Price = 1_000_000_000_000, ExpiresAtMs = 99, IsMine = true });
    }

    [Fact]
    public void BuyListingAndBrowse_RoundTrip()
    {
        Guid id = Guid.CreateVersion7();
        using BuyListing buy = new() { ListingId = id };
        using BrowseListings browse = new() { Page = 3 };
        browse.ItemIds.AddRange([200, 201]);

        BuyListing.Deserialize(buy.Serialize()).ListingId.Should().Be(id);
        BrowseListings copy = BrowseListings.Deserialize(browse.Serialize());
        copy.Page.Should().Be(3);
        copy.ItemIds.Should().Equal(200, 201);
    }

    [Fact]
    public void LookChanged_CarriesOnlyIds()
    {
        using LookChanged packet = new();
        packet.Look.PlayerId = 7;
        packet.Look.SkinId = 2;
        packet.Look.HairStyleId = 5;
        packet.Look.HairDyeId = 3;
        packet.Look.ShirtItemId = 300;
        packet.Look.HatItemId = 341;

        LookChanged copy = LookChanged.Deserialize(packet.Serialize());

        copy.Look.Should().BeEquivalentTo(new LookRow { PlayerId = 7, SkinId = 2, HairStyleId = 5, HairDyeId = 3, ShirtItemId = 300, PantsItemId = 0, HatItemId = 341 });
    }

    [Fact]
    public void TradeState_WithBothOffers_RoundTrips()
    {
        using TradeState packet = new() { OtherPlayerId = 4, MyCoins = 10, OtherCoins = 20, MyConfirmed = true, OtherConfirmed = false };
        packet.MyItems.Add(new TradeItemRow { ItemId = 200, Quantity = 3 });
        packet.OtherItems.Add(new TradeItemRow { ItemId = 201, Quantity = 4 });

        TradeState copy = TradeState.Deserialize(packet.Serialize());

        (copy.OtherPlayerId, copy.MyCoins, copy.OtherCoins, copy.MyConfirmed, copy.OtherConfirmed).Should().Be((4u, 10L, 20L, true, false));
        copy.MyItems.Should().ContainSingle().Which.Quantity.Should().Be(3);
        copy.OtherItems.Should().ContainSingle().Which.ItemId.Should().Be(201);
    }

    [Fact]
    public void CoopSnapshot_WithMembersStockLogAndInvites_RoundTrips()
    {
        Guid member = Guid.CreateVersion7();
        using CoopSnapshot packet = new() { HasCoop = true, Name = "i18n{\"vi\":\"Vườn\",\"en\":\"Garden\"}", Role = CoopRole.Founder, ServerNowMs = 5 };
        packet.Members.Add(new CoopMemberRow { MemberId = member, Role = CoopRole.Founder, IsMe = true, Online = true });
        packet.Stock.Add(new CoopStockRow { ItemId = 200, Quantity = 12, Available = 12 });
        packet.Log.Add(new CoopLogRow { MemberId = member, ItemId = 200, Delta = -3, AtMs = 77 });
        packet.Invites.Add(new CoopInviteRow { InviteId = member, CoopName = "x" });

        CoopSnapshot copy = CoopSnapshot.Deserialize(packet.Serialize());

        copy.Name.Should().Contain("Garden");
        copy.Role.Should().Be(CoopRole.Founder);
        copy.Members.Should().ContainSingle().Which.MemberId.Should().Be(member);
        copy.Stock.Should().ContainSingle().Which.Available.Should().Be(12);
        copy.Log.Should().ContainSingle().Which.Delta.Should().Be(-3);
        copy.Invites.Should().ContainSingle().Which.CoopName.Should().Be("x");
    }

    [Fact]
    public void GroundItemsSnapshot_RoundTrips()
    {
        using GroundItemsSnapshot packet = new() { MapId = "town" };
        packet.Items.Add(new GroundItemRow { Id = 5_000_000_001, ItemId = 202, Quantity = 3, X = 2000, Y = 1700 });
        packet.Items.Add(new GroundItemRow { Id = 2, ItemId = 200, Quantity = 1, X = 4, Y = 5 });

        GroundItemsSnapshot copy = GroundItemsSnapshot.Deserialize(packet.Serialize());

        copy.MapId.Should().Be("town");
        copy.Items.Should().HaveCount(2);
        copy.Items[0].Should().BeEquivalentTo(new GroundItemRow { Id = 5_000_000_001, ItemId = 202, Quantity = 3, X = 2000, Y = 1700 });
    }

    [Fact]
    public void GroundItemSpawnedAndTaken_RoundTrip()
    {
        using GroundItemSpawned spawned = new() { MapId = "farm", Item = new GroundItemRow { Id = 9, ItemId = 201, Quantity = 4, X = 33, Y = 44 } };
        using GroundItemTaken taken = new() { MapId = "farm", Id = 9, Reason = GroundItemGone.PickedUp, ToX = 70, ToY = 80 };

        GroundItemSpawned spawnedCopy = GroundItemSpawned.Deserialize(spawned.Serialize());
        GroundItemTaken takenCopy = GroundItemTaken.Deserialize(taken.Serialize());

        spawnedCopy.MapId.Should().Be("farm");
        spawnedCopy.Item.Should().BeEquivalentTo(new GroundItemRow { Id = 9, ItemId = 201, Quantity = 4, X = 33, Y = 44 });
        (takenCopy.MapId, takenCopy.Id, takenCopy.Reason, takenCopy.ToX, takenCopy.ToY).Should().Be(("farm", 9L, GroundItemGone.PickedUp, (ushort)70, (ushort)80));
    }

    [Fact]
    public void GroundItemOpCodes_AreInTheGroundItemSubRangeOfTheWorldBand()
    {
        ushort[] codes = [GroundItemOpCode.GetGroundItems, GroundItemOpCode.GroundItemsSnapshot, GroundItemOpCode.GroundItemSpawned, GroundItemOpCode.GroundItemTaken];

        codes.Should().OnlyContain(c => c >= 0x7200 && c <= 0x72FF).And.OnlyHaveUniqueItems();
    }
}
