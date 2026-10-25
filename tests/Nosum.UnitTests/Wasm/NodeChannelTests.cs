// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Node;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class NodeChannelTests
{
    private static NodeRow Row(int id, int x, int y, NodeKind kind, byte hits, byte variant = 0, byte stage = 0)
        => new() { Id = id, X = (ushort)x, Y = (ushort)y, Kind = (byte)kind, Variant = variant, Stage = stage, Hits = hits };

    private static FakeGameSocket SocketWith(string mapId, byte water, params NodeRow[] rows)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            NodeSnapshot snapshot = new() { MapId = mapId, WaterLeft = water, WaterCapacity = 40 };
            snapshot.Nodes.AddRange(rows);
            return snapshot;
        });
        return socket;
    }

    private static List<NodeInfo> Mirror(NodeChannel channel)
    {
        List<NodeInfo> nodes = [];
        using (channel.Read())
        {
            for (int i = 0; i < channel.Count; i++)
            {
                nodes.Add(channel.At(i));
            }
        }

        return nodes;
    }

    [Fact]
    public void Load_StoresTheNodesTheMapAndTheWater_AndBumpsTheVersion()
    {
        FakeGameSocket socket = SocketWith("forest", 12, Row(1, 3, 4, NodeKind.Stone, 1), Row(2, 5, 6, NodeKind.Tree, 10, TreeVariant.Pine, TreeStage.Grown));
        using NodeChannel channel = new(socket);
        int before = channel.Version;

        channel.Load();

        channel.MapId.Should().Be("forest");
        channel.WaterLeft.Should().Be(12);
        channel.WaterCapacity.Should().Be(40);
        channel.Version.Should().NotBe(before);
        Mirror(channel).Should().BeEquivalentTo(
        [
            new NodeInfo(1, 3, 4, (byte)NodeKind.Stone, 0, 0, 1, 0),
            new NodeInfo(2, 5, 6, (byte)NodeKind.Tree, TreeVariant.Pine, TreeStage.Grown, 10, 0),
        ]);
    }

    [Fact]
    public void PushedChange_UpsertsAndRemoves_AndIgnoresAnotherMap()
    {
        FakeGameSocket socket = SocketWith("forest", 40, Row(1, 3, 4, NodeKind.Stone, 1), Row(2, 5, 6, NodeKind.Twig, 1));
        using NodeChannel channel = new(socket);
        channel.Load();
        int version = channel.Version;

        using NodeChanged late = new() { MapId = "town" };
        late.Removed.Add(1);
        socket.Push(late);
        channel.Version.Should().Be(version);
        channel.Count.Should().Be(2);

        using NodeChanged push = new() { MapId = "forest" };
        push.Upserted.Add(Row(3, 7, 7, NodeKind.Weed, 1));
        push.Removed.Add(1);
        socket.Push(push);

        channel.Version.Should().NotBe(version);
        Mirror(channel).Select(n => n.Id).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void PushedChange_ThatTakesAHit_RecordsWhenSoTheSceneCanShake()
    {
        long now = 5_000;
        FakeGameSocket socket = SocketWith("forest", 40, Row(2, 5, 6, NodeKind.Tree, 10, TreeVariant.Oak, TreeStage.Grown));
        using NodeChannel channel = new(socket, () => now);
        channel.Load();
        Mirror(channel)[0].HitAtMs.Should().Be(0);

        using NodeChanged hit = new() { MapId = "forest" };
        hit.Upserted.Add(Row(2, 5, 6, NodeKind.Tree, 9, TreeVariant.Oak, TreeStage.Grown));
        socket.Push(hit);

        Mirror(channel)[0].Should().Match<NodeInfo>(n => n.Hits == 9 && n.HitAtMs == 5_000);
    }

    [Fact]
    public void ToolState_UpdatesTheWater_AndRaisesTheEvent()
    {
        FakeGameSocket socket = SocketWith("forest", 40);
        using NodeChannel channel = new(socket);
        int raised = 0;
        channel.ToolChanged += () => raised++;

        using ToolState state = new() { WaterLeft = 7, WaterCapacity = 40 };
        socket.Push(state);

        channel.WaterLeft.Should().Be(7);
        raised.Should().Be(1);
    }

    [Fact]
    public void Mirror_BlockingNodesBlockTheGrid_AndWeedsAndGrassDoNot()
    {
        FakeGameSocket socket = SocketWith(
            "forest",
            40,
            Row(1, 1, 1, NodeKind.Stone, 1),
            Row(2, 2, 1, NodeKind.Weed, 1),
            Row(3, 3, 1, NodeKind.Grass, 1),
            Row(4, 4, 1, NodeKind.Tree, 10, TreeVariant.Oak, TreeStage.Grown));
        using NodeChannel channel = new(socket);
        channel.Load();
        WalkGrid open = new(8, 4, 16, new bool[32]);

        // The client builds its prediction grid like this (MapScene.NodeGridOf), with the same NodeField the server uses.
        NodeField field = new();
        foreach (NodeInfo node in Mirror(channel))
        {
            field.Add(new ResourceNode(node.Id, node.X, node.Y, (NodeKind)node.Kind, node.Variant, node.Stage, node.Hits));
        }

        WalkGrid grid = field.BuildGrid(open);
        grid.IsBlocked(1, 1).Should().BeTrue();
        grid.IsBlocked(2, 1).Should().BeFalse();
        grid.IsBlocked(3, 1).Should().BeFalse();
        grid.IsBlocked(4, 1).Should().BeTrue();
        open.IsBlocked(1, 1).Should().BeFalse("the map's own grid is never changed");
    }

    [Fact]
    public void FarmChannel_DropsTilesAPickaxeCleared()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm" };
            snapshot.Tiles.Add(new FarmTileRow { X = 1, Y = 1 });
            snapshot.Tiles.Add(new FarmTileRow { X = 2, Y = 2 });
            snapshot.Tiles.Add(new FarmTileRow { X = 3, Y = 3 });
            return snapshot;
        });
        using FarmChannel farm = new(socket);
        farm.Load();

        using FarmTilesCleared cleared = new() { MapId = "farm" };
        cleared.Tiles.Add(new TileRow { X = 1, Y = 1 });
        socket.Push(cleared);

        farm.Count.Should().Be(2);
        farm.TryGetTile(1, 1, out _).Should().BeFalse();
        farm.TryGetTile(2, 2, out _).Should().BeTrue();
        farm.TryGetTile(3, 3, out _).Should().BeTrue();
    }

    [Fact]
    public void ChunkState_AddsTheChunksNodesAndReplacesAnyOlderOnes()
    {
        FakeGameSocket socket = SocketWith("forest", 40, Row(1, 3, 4, NodeKind.Stone, 1));
        using NodeChannel channel = new(socket);
        channel.Load();
        int before = channel.Version;
        using ChunkState state = new() { MapId = "forest", ChunkX = 4, ChunkY = 2 };
        state.Nodes.Add(Row(2, 69, 37, NodeKind.Stone, 3));
        state.Nodes.Add(Row(3, 70, 37, NodeKind.Weed, 1));
        using ChunkState again = new() { MapId = "forest", ChunkX = 4, ChunkY = 2 };
        again.Nodes.Add(Row(3, 70, 37, NodeKind.Weed, 1));

        socket.Push(state);
        socket.Push(again);

        List<NodeInfo> nodes = Mirror(channel);
        nodes.Select(n => n.Id).Should().BeEquivalentTo([1, 3], "the second state of chunk (4, 2) replaces the first; the chunk (0, 0) stone stays");
        channel.Version.Should().NotBe(before);
    }

    [Fact]
    public void ChunkLeft_DropsOnlyThatChunksNodes_AndKeepsTheIndexInStep()
    {
        FakeGameSocket socket = SocketWith("forest", 40, Row(1, 3, 4, NodeKind.Stone, 1), Row(2, 69, 37, NodeKind.Stone, 3), Row(3, 5, 6, NodeKind.Weed, 1));
        using NodeChannel channel = new(socket);
        channel.Load();
        using ChunkLeft left = new() { MapId = "forest", ChunkX = 4, ChunkY = 2 };
        using ChunkLeft other = new() { MapId = "town", ChunkX = 0, ChunkY = 0 };

        socket.Push(other);
        Mirror(channel).Should().HaveCount(3);
        socket.Push(left);

        Mirror(channel).Select(n => n.Id).Should().BeEquivalentTo([1, 3]);

        // A later change still finds its node after the list was compacted.
        using NodeChanged gone = new() { MapId = "forest" };
        gone.Removed.Add(3);
        socket.Push(gone);
        Mirror(channel).Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void ChunkState_ForAnotherMap_IsIgnored()
    {
        FakeGameSocket socket = SocketWith("forest", 40);
        using NodeChannel channel = new(socket);
        channel.Load();
        using ChunkState state = new() { MapId = "town", ChunkX = 0, ChunkY = 0 };
        state.Nodes.Add(Row(1, 3, 4, NodeKind.Stone, 1));

        socket.Push(state);

        Mirror(channel).Should().BeEmpty();
    }
}
