// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Placeable;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class PlaceableChannelTests
{
    private static PlacedObjectRow Row(int id, int item, int x, int y, int rotation = 0) => new() { Id = id, ItemId = item, TileX = (ushort)x, TileY = (ushort)y, Rotation = (byte)rotation };

    [Fact]
    public void Load_StoresTheObjectsAndTheMap_AndBumpsTheVersion()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            PlacedSnapshot snapshot = new() { MapId = "farmhouse" };
            snapshot.Objects.Add(Row(4, ItemCatalog.OakChair, 5, 6, 2));
            return snapshot;
        });
        using PlaceableChannel channel = new(socket);
        int before = channel.Version;

        channel.Load();

        channel.MapId.Should().Be("farmhouse");
        channel.Count.Should().Be(1);
        using (channel.Read())
        {
            channel.At(0).Should().Be(new PlacedObjectInfo(4, ItemCatalog.OakChair, 5, 6, 2));
        }

        channel.Version.Should().NotBe(before);
    }

    [Fact]
    public void PushedChange_AddsReplacesAndRemoves()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            PlacedSnapshot snapshot = new() { MapId = "farmhouse" };
            snapshot.Objects.Add(Row(1, ItemCatalog.OakChair, 2, 2));
            snapshot.Objects.Add(Row(2, ItemCatalog.OakTable, 4, 4));
            return snapshot;
        });
        using PlaceableChannel channel = new(socket);
        channel.Load();
        int version = channel.Version;
        PlacedChanged push = new() { MapId = "farmhouse" };
        push.Upserted.Add(Row(1, ItemCatalog.OakChair, 7, 7, 1));
        push.Upserted.Add(Row(3, ItemCatalog.FloorLamp, 8, 8));
        push.Removed.Add(2);

        socket.Push(push);

        channel.Version.Should().NotBe(version);
        List<PlacedObjectInfo> items = [];
        using (channel.Read())
        {
            for (int i = 0; i < channel.Count; i++)
            {
                items.Add(channel.At(i));
            }
        }

        items.Should().BeEquivalentTo(
        [
            new PlacedObjectInfo(1, ItemCatalog.OakChair, 7, 7, 1),
            new PlacedObjectInfo(3, ItemCatalog.FloorLamp, 8, 8, 0),
        ]);
        push.Dispose();
    }

    [Fact]
    public void PushedChange_ForAnotherMap_IsIgnored()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new PlacedSnapshot { MapId = "farmhouse" });
        using PlaceableChannel channel = new(socket);
        channel.Load();
        PlacedChanged push = new() { MapId = "town" };
        push.Upserted.Add(Row(1, ItemCatalog.OakChair, 2, 2));

        socket.Push(push);

        channel.Count.Should().Be(0);
        push.Dispose();
    }

    [Fact]
    public void Place_SendsTheSlotTileAndRotation_AndReportsTheServersAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new PlaceResult { Status = PlaceStatus.Blocked });
        using PlaceableChannel channel = new(socket);
        List<PlaceStatus> answers = [];
        channel.Completed += answers.Add;

        channel.Place(5, 3, 4, 2);

        PlaceObject request = (PlaceObject)socket.Requests[0];
        (request.Slot, request.TileX, request.TileY, request.Rotation).Should().Be(((byte)5, 3, 4, (byte)2));
        answers.Should().Equal(PlaceStatus.Blocked);
    }

    [Fact]
    public void ChunkState_ReplacesTheChunksObjectsAndKeepsTheOthers()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            PlacedSnapshot snapshot = new() { MapId = "farmhouse" };
            snapshot.Objects.Add(Row(1, ItemCatalog.OakChair, 2, 2));
            snapshot.Objects.Add(Row(2, ItemCatalog.OakChair, 18, 3));
            return snapshot;
        });
        using PlaceableChannel channel = new(socket);
        channel.Load();
        int before = channel.Version;
        ChunkState state = new() { MapId = "farmhouse", ChunkX = 1, ChunkY = 0 };
        state.Placed.Add(Row(3, ItemCatalog.OakChair, 30, 4));

        socket.Push(state);

        channel.Version.Should().NotBe(before);
        List<int> ids = [];
        using (channel.Read())
        {
            for (int i = 0; i < channel.Count; i++)
            {
                ids.Add(channel.At(i).Id);
            }
        }

        ids.Should().BeEquivalentTo([1, 3]);
        state.Dispose();
    }

    [Fact]
    public void ChunkLeft_DropsTheObjectsOfThatChunkOnly()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            PlacedSnapshot snapshot = new() { MapId = "farmhouse" };
            snapshot.Objects.Add(Row(1, ItemCatalog.OakChair, 2, 2));
            snapshot.Objects.Add(Row(2, ItemCatalog.OakChair, 18, 3));
            return snapshot;
        });
        using PlaceableChannel channel = new(socket);
        channel.Load();
        ChunkLeft left = new() { MapId = "farmhouse", ChunkX = 1, ChunkY = 0 };

        socket.Push(left);

        channel.Count.Should().Be(1);
        using (channel.Read())
        {
            channel.At(0).Id.Should().Be(1);
        }

        left.Dispose();
    }
}
