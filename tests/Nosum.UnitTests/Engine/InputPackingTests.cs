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
    public void Unpack_WhatJavaScriptPacks_MatchesTheEdgeBits()
    {
        // renderer.js: keys | (edges << 8) with Digit3 = 8 and KeyE = 1.
        int fromJs = 2 | ((8 | 1) << 8);

        InputPacking.Unpack(fromJs, out InputState held, out InputEdges edges);

        held.Should().Be(InputState.Down);
        edges.Should().Be(InputEdges.Number3 | InputEdges.Action);
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
