// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Labels;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>docs/adr/ADR-091: a hazard label is not placed on top of a label of the same text.</summary>
public sealed class HazardLabelRulesTests
{
    [Fact]
    public void Clashes_SameTextOnTheSameTile_IsTrue()
    {
        HazardLabelRules.Clashes(7, 320, 160, 7, 320, 160).Should().BeTrue();
    }

    [Fact]
    public void Clashes_SameTextOneTileAway_IsTrue()
    {
        HazardLabelRules.Clashes(7, 336, 160, 7, 320, 160).Should().BeTrue();
    }

    [Fact]
    public void Clashes_SameTextBeyondTheSpan_IsFalse()
    {
        HazardLabelRules.Clashes(7, 320 + HazardLabelRules.SpanX, 160, 7, 320, 160).Should().BeFalse();
    }

    [Fact]
    public void Clashes_SameTextRowBelowTheSpan_IsFalse()
    {
        HazardLabelRules.Clashes(7, 320, 160 + HazardLabelRules.SpanY, 7, 320, 160).Should().BeFalse();
    }

    [Fact]
    public void Clashes_DifferentTextOnTheSameTile_IsFalse()
    {
        HazardLabelRules.Clashes(7, 320, 160, 8, 320, 160).Should().BeFalse("a spike trap and a magma fissure are different labels");
    }
}
