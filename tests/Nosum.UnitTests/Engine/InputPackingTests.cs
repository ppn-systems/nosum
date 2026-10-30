// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Core;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class InputPackingTests
{
    [Fact]
    public void PackUnpack_HeldAndEdges_RoundTrip()
    {
        int packed = InputPacking.Pack(InputState.Up | InputState.Right, InputEdges.Action | InputEdges.Number9 | InputEdges.Inventory);

        InputPacking.Unpack(packed, out InputState held, out InputEdges edges);

        held.Should().Be(InputState.Up | InputState.Right);
        edges.Should().Be(InputEdges.Action | InputEdges.Number9 | InputEdges.Inventory);
    }

    [Fact]
    public void Unpack_CloseAndHelpKeys_MatchWhatRendererJsPacks()
    {
        // renderer.js: Escape = 262144 and KeyH = 524288, both shifted left by 4 with the held directions.
        int fromJs = 1 | ((262144 | 524288) << 4);

        InputPacking.Unpack(fromJs, out InputState held, out InputEdges edges);

        held.Should().Be(InputState.Up);
        edges.Should().Be(InputEdges.Close | InputEdges.Help);
    }

    [Fact]
    public void Unpack_WhatJavaScriptPacks_MatchesTheEdgeBits()
    {
        // renderer.js: keys | (edges << 4) with Digit3 = 8 and KeyE = 1.
        int fromJs = 2 | ((8 | 1) << 4);

        InputPacking.Unpack(fromJs, out InputState held, out InputEdges edges);

        held.Should().Be(InputState.Down);
        edges.Should().Be(InputEdges.Number3 | InputEdges.Action);
    }

    [Fact]
    public void Unpack_MinimapWorldMapAndJournalKeys_MatchWhatRendererJsPacks()
    {
        // renderer.js: KeyM = 16777216, Tab = 33554432 and KeyJ = 67108864, shifted left by 4 with the held directions.
        int fromJs = 4 | ((16777216 | 33554432 | 67108864) << 4);
        InputPacking.Unpack(fromJs, out InputState held, out InputEdges edges);
        held.Should().Be(InputState.Left);
        edges.Should().Be(InputEdges.Minimap | InputEdges.WorldMap | InputEdges.Journal);
    }

    [Fact]
    public void Pack_AllHeldDirectionsAndTheTopEdge_RoundTrip()
    {
        int packed = InputPacking.Pack(InputState.Up | InputState.Down | InputState.Left | InputState.Right, InputEdges.Journal);
        InputPacking.Unpack(packed, out InputState held, out InputEdges edges);
        held.Should().Be(InputState.Up | InputState.Down | InputState.Left | InputState.Right);
        edges.Should().Be(InputEdges.Journal);
    }

    [Theory]
    [InlineData(InputEdges.None, -1)]
    [InlineData(InputEdges.Action, -1)]
    [InlineData(InputEdges.Number1, 0)]
    [InlineData(InputEdges.Number9, 8)]
    [InlineData(InputEdges.Number4 | InputEdges.Number7, 3)]
    public void NumberSlot_ReturnsTheLowestNumberKeyPressed(InputEdges edges, int expected)
        => InputPacking.NumberSlot(edges).Should().Be(expected);
}
