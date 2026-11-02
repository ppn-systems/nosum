// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Labels;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>docs/adr/ADR-091 and ADR-110: a label is not placed on top of a label of the same text placed earlier in the frame.</summary>
public sealed class PlacedLabelSetTests
{
    [Fact]
    public void Take_FirstLabel_IsPlaced()
    {
        var set = new PlacedLabelSet();

        set.Take(7, 320, 160).Should().BeTrue();
    }

    [Fact]
    public void Take_SameTextWithinSpan_IsRefused()
    {
        var set = new PlacedLabelSet();
        set.Take(7, 320, 160);

        // An elite pack: the next elite is one tile (16 px) to the right, so its badge would sit on the first.
        set.Take(7, 336, 160).Should().BeFalse();
    }

    [Fact]
    public void Take_DifferentTextWithinSpan_IsPlaced()
    {
        var set = new PlacedLabelSet();
        set.Take(7, 320, 160);

        set.Take(8, 336, 160).Should().BeTrue();
    }

    [Fact]
    public void Take_SameTextBeyondSpan_IsPlaced()
    {
        var set = new PlacedLabelSet();
        set.Take(7, 320, 160);

        set.Take(7, 320 + HazardLabelRules.SpanX, 160).Should().BeTrue();
    }

    [Fact]
    public void Take_AfterReset_PlacesTheSameLabelAgain()
    {
        var set = new PlacedLabelSet();
        set.Take(7, 320, 160);

        set.Reset();

        set.Take(7, 336, 160).Should().BeTrue();
    }

    [Fact]
    public void Take_PastCapacity_StillPlacesLabelsWithoutANeighbour()
    {
        var set = new PlacedLabelSet();
        for (int i = 0; i < PlacedLabelSet.Capacity + 10; i++)
        {
            // Each label is far from the others, so none is refused and none needs the array's last slot.
            set.Take(100 + i, i * 400, 0).Should().BeTrue();
        }
    }
}
