// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Node;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The resource-node packets (docs/adr/ADR-021 D9): round trips and the opcode sub-range.</summary>
public sealed class NodePacketTests
{
    [Fact]
    public void NodeSnapshot_RoundTrips()
    {
        using NodeSnapshot packet = new() { MapId = "forest", WaterLeft = 17, WaterCapacity = 40 };
        packet.Nodes.Add(new NodeRow { Id = 70_000, X = 129, Y = 3, Kind = 1, Variant = 2, Stage = 3, Hits = 10 });
        packet.Nodes.Add(new NodeRow { Id = 70_001, X = 0, Y = 119, Kind = 7, Hits = 1 });

        NodeSnapshot copy = NodeSnapshot.Deserialize(packet.Serialize());

        (copy.MapId, copy.WaterLeft, copy.WaterCapacity).Should().Be(("forest", (byte)17, (byte)40));
        copy.Nodes.Should().HaveCount(2);
        copy.Nodes[0].Should().BeEquivalentTo(new NodeRow { Id = 70_000, X = 129, Y = 3, Kind = 1, Variant = 2, Stage = 3, Hits = 10 });
        copy.Nodes[1].Should().BeEquivalentTo(new NodeRow { Id = 70_001, X = 0, Y = 119, Kind = 7, Hits = 1 });
    }

    [Fact]
    public void NodeChanged_ToolState_FarmTilesCleared_GetNodes_RoundTrip()
    {
        using NodeChanged changed = new() { MapId = "farm" };
        changed.Upserted.Add(new NodeRow { Id = 5, X = 8, Y = 9, Kind = 2, Variant = 1, Hits = 2 });
        changed.Removed.AddRange([6, 7]);
        using ToolState tool = new() { WaterLeft = 0, WaterCapacity = 40 };
        using FarmTilesCleared cleared = new() { MapId = "farm" };
        cleared.Tiles.Add(new TileRow { X = 61, Y = 17 });
        using GetNodes get = new();

        NodeChanged changedCopy = NodeChanged.Deserialize(changed.Serialize());
        ToolState toolCopy = ToolState.Deserialize(tool.Serialize());
        FarmTilesCleared clearedCopy = FarmTilesCleared.Deserialize(cleared.Serialize());

        changedCopy.Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new NodeRow { Id = 5, X = 8, Y = 9, Kind = 2, Variant = 1, Hits = 2 });
        changedCopy.Removed.Should().Equal(6, 7);
        (toolCopy.WaterLeft, toolCopy.WaterCapacity).Should().Be(((byte)0, (byte)40));
        clearedCopy.Tiles.Should().ContainSingle().Which.Should().BeEquivalentTo(new TileRow { X = 61, Y = 17 });
        GetNodes.Deserialize(get.Serialize()).Reserved.Should().Be(0);
    }

    [Fact]
    public void NodePackets_UseTheirSubRangeOfTheWorldBand_WithoutClashes()
    {
        ushort[] opcodes = [.. typeof(NodeOpCode).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];

        opcodes.Should().OnlyHaveUniqueItems().And.OnlyContain(o => o >= 0x7100 && o <= 0x71FF);
        GetNodes.StaticOpCode.Should().Be(NodeOpCode.GetNodes).And.BeInRange((ushort)0x7100, (ushort)0x713F);
        new[] { NodeSnapshot.StaticOpCode, NodeChanged.StaticOpCode, ToolState.StaticOpCode, FarmTilesCleared.StaticOpCode }
            .Should().OnlyContain(o => o >= 0x7140 && o <= 0x717F).And.OnlyHaveUniqueItems();
    }
}
