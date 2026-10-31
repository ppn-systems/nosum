// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Core;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>The run key is a held bit above the directions (docs/adr/ADR-081 D3); the edges stay above all held bits.</summary>
public sealed class RunInputPackingTests
{
    [Fact]
    public void Pack_RunWithADirectionAndAnEdge_RoundTrips()
    {
        int packed = InputPacking.Pack(InputState.Right | InputState.Run, InputEdges.Action | InputEdges.Minimap);

        InputPacking.Unpack(packed, out InputState held, out InputEdges edges);

        held.Should().Be(InputState.Right | InputState.Run);
        edges.Should().Be(InputEdges.Action | InputEdges.Minimap);
    }

    [Fact]
    public void Unpack_RunBitFromTheBrowser_IsRunAndNotAnEdge()
    {
        // renderer.js sends the held bits in the low HeldBits and the pressed edges above them: Shift is 16, edge Action is 1.
        int fromJs = 16 | (1 << InputPacking.HeldBits);

        InputPacking.Unpack(fromJs, out InputState held, out InputEdges edges);

        held.Should().Be(InputState.Run);
        edges.Should().Be(InputEdges.Action);
    }
}
