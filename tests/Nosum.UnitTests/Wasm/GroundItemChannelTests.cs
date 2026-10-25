// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.GroundItem;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class GroundItemChannelTests
{
    private static GroundItemRow Row(long id, int item, int quantity, int x, int y) => new() { Id = id, ItemId = item, Quantity = quantity, X = (ushort)x, Y = (ushort)y };

    private static GroundItemChannel Loaded(FakeGameSocket socket, params GroundItemRow[] rows)
    {
        socket.ReplyWith(_ =>
        {
            GroundItemsSnapshot snapshot = new() { MapId = "town" };
            snapshot.Items.AddRange(rows);
            return snapshot;
        });
        GroundItemChannel channel = new(socket);
        channel.Load();
        return channel;
    }

    private static List<GroundItemInfo> Items(GroundItemChannel channel)
    {
        List<GroundItemInfo> items = [];
        using (channel.Read())
        {
            for (int i = 0; i < channel.Count; i++)
            {
                items.Add(channel.At(i));
            }
        }

        return items;
    }

    [Fact]
    public void Load_StoresTheItemsAndTheMap_AndBumpsTheVersion()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = new(socket);
        int before = channel.Version;
        socket.ReplyWith(_ =>
        {
            GroundItemsSnapshot snapshot = new() { MapId = "town" };
            snapshot.Items.Add(Row(5, ItemCatalog.Parsnip, 2, 100, 120));
            return snapshot;
        });

        channel.Load();

        channel.MapId.Should().Be("town");
        Items(channel).Should().ContainSingle().Which.Should().Match<GroundItemInfo>(i => i.Id == 5 && i.ItemId == ItemCatalog.Parsnip && i.Quantity == 2 && i.X == 100 && i.Y == 120);
        channel.Version.Should().NotBe(before);
    }

    [Fact]
    public void Spawned_AddsTheItem_AndAnotherMapsPushIsIgnored()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket);
        using GroundItemSpawned here = new() { MapId = "town", Item = Row(1, ItemCatalog.Radish, 1, 10, 20) };
        using GroundItemSpawned elsewhere = new() { MapId = "farm", Item = Row(2, ItemCatalog.Radish, 1, 10, 20) };

        socket.Push(elsewhere);
        socket.Push(here);

        Items(channel).Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void Taken_PickedUp_RemovesTheItemAndLeavesAFlightToTheTaker()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(7, ItemCatalog.Cauliflower, 1, 50, 60));
        using GroundItemTaken push = new() { MapId = "town", Id = 7, Reason = GroundItemGone.PickedUp, ToX = 70, ToY = 80 };

        socket.Push(push);

        Items(channel).Should().BeEmpty();
        using (channel.Read())
        {
            FlyingItem flight = channel.FlightAt(0);
            (flight.ItemId, flight.FromX, flight.FromY, flight.ToX, flight.ToY).Should().Be((ItemCatalog.Cauliflower, 50, 60, 70, 80));
        }
    }

    [Fact]
    public void Taken_PickedUp_RaisesPickedUpWithTheTakersPlace()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(7, ItemCatalog.Cauliflower, 1, 50, 60));
        List<(int X, int Y)> heard = [];
        channel.PickedUp += (x, y) => heard.Add((x, y));
        using GroundItemTaken push = new() { MapId = "town", Id = 7, Reason = GroundItemGone.PickedUp, ToX = 70, ToY = 80 };

        socket.Push(push);

        heard.Should().Equal([(70, 80)]);
    }

    [Fact]
    public void Taken_Expired_DoesNotRaisePickedUp()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(7, ItemCatalog.Cauliflower, 1, 50, 60));
        int heard = 0;
        channel.PickedUp += (_, _) => heard++;
        using GroundItemTaken push = new() { MapId = "town", Id = 7, Reason = GroundItemGone.Expired };

        socket.Push(push);

        heard.Should().Be(0);
    }

    [Fact]
    public void Taken_Expired_RemovesTheItemWithoutAFlight()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(7, ItemCatalog.Cauliflower, 1, 50, 60));
        using GroundItemTaken push = new() { MapId = "town", Id = 7, Reason = GroundItemGone.Expired };

        socket.Push(push);

        Items(channel).Should().BeEmpty();
        using (channel.Read())
        {
            channel.FlightAt(0).ItemId.Should().Be(0);
        }
    }

    [Fact]
    public void Taken_AnItemWeNeverSaw_ChangesNothing()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(7, ItemCatalog.Cauliflower, 1, 50, 60));
        int version = channel.Version;
        using GroundItemTaken push = new() { MapId = "town", Id = 99, Reason = GroundItemGone.PickedUp };

        socket.Push(push);

        Items(channel).Should().ContainSingle();
        channel.Version.Should().Be(version);
    }

    [Fact]
    public void Load_AfterAMapChange_ReplacesTheItemsAndDropsOldFlights()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(7, ItemCatalog.Cauliflower, 1, 50, 60));
        using GroundItemTaken push = new() { MapId = "town", Id = 7, Reason = GroundItemGone.PickedUp, ToX = 1, ToY = 1 };
        socket.Push(push);
        socket.ReplyWith(_ => new GroundItemsSnapshot { MapId = "farm" });

        channel.Load();

        channel.MapId.Should().Be("farm");
        Items(channel).Should().BeEmpty();
        using (channel.Read())
        {
            channel.FlightAt(0).ItemId.Should().Be(0);
        }
    }

    [Fact]
    public void ChunkState_AddsTheChunksItemsAndReplacesAnyOlderOnes()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(1, ItemCatalog.Parsnip, 1, 20, 20));
        using ChunkState state = new() { MapId = "town", ChunkX = 4, ChunkY = 2 };
        state.GroundItems.Add(Row(2, ItemCatalog.Wood, 3, 1100, 600));
        state.GroundItems.Add(Row(3, ItemCatalog.Wood, 1, 1200, 640));
        using ChunkState again = new() { MapId = "town", ChunkX = 4, ChunkY = 2 };
        again.GroundItems.Add(Row(3, ItemCatalog.Wood, 5, 1200, 640));

        socket.Push(state);
        socket.Push(again);

        List<GroundItemInfo> items = Items(channel);
        items.Should().HaveCount(2, "the second state of chunk (4, 2) replaces the first; the chunk (0, 0) item stays");
        items.Should().Contain(i => i.Id == 1);
        items.Single(i => i.Id == 3).Quantity.Should().Be(5);
    }

    [Fact]
    public void ChunkLeft_DropsOnlyThatChunksItems()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket, Row(1, ItemCatalog.Parsnip, 1, 20, 20), Row(2, ItemCatalog.Wood, 1, 1100, 600));
        int before = channel.Version;
        using ChunkLeft left = new() { MapId = "town", ChunkX = 4, ChunkY = 2 };
        using ChunkLeft empty = new() { MapId = "town", ChunkX = 9, ChunkY = 9 };
        using ChunkLeft other = new() { MapId = "farm", ChunkX = 0, ChunkY = 0 };

        socket.Push(empty);
        socket.Push(other);
        channel.Version.Should().Be(before, "a chunk with nothing in it, or another map's chunk, changes nothing");
        socket.Push(left);

        Items(channel).Should().ContainSingle().Which.Id.Should().Be(1);
        channel.Version.Should().NotBe(before);
    }

    [Fact]
    public void ChunkState_ForAnotherMap_IsIgnored()
    {
        FakeGameSocket socket = new();
        using GroundItemChannel channel = Loaded(socket);
        using ChunkState state = new() { MapId = "farm", ChunkX = 0, ChunkY = 0 };
        state.GroundItems.Add(Row(1, ItemCatalog.Wood, 1, 20, 20));

        socket.Push(state);

        Items(channel).Should().BeEmpty();
    }
}
