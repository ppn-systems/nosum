// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.S2C;
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
}
