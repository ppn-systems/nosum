// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Inventory;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The tailor's backpack sizes and prices (docs/adr/ADR-081).</summary>
public sealed class BackpackRulesTests
{
    [Theory]
    [InlineData(36, 45, 600)]
    [InlineData(45, 54, 1_500)]
    public void TryNext_EachSizeGrowsOneRowForItsPrice(int current, int capacity, int coins)
    {
        BackpackRules.TryNext(current, out BackpackQuote quote).Should().BeTrue();

        quote.Should().Be(new BackpackQuote(capacity, coins));
    }

    [Fact]
    public void TryNext_TheLargestBackpack_HasNoNextSize()
    {
        BackpackRules.TryNext(BackpackRules.MaxCapacity, out BackpackQuote quote).Should().BeFalse();

        quote.Should().Be(default(BackpackQuote));
    }

    [Fact]
    public void MaxCapacity_IsSixRowsOfNine()
    {
        BackpackRules.MaxCapacity.Should().Be(54);
        (BackpackRules.MaxCapacity % 9).Should().Be(0);
    }

    [Theory]
    [InlineData(36, 45)]
    [InlineData(45, 54)]
    public void MayGrow_ToALargerSizeUpToTheMaximum_IsAllowed(int current, int target)
    {
        BackpackRules.MayGrow(current, target).Should().BeTrue();
    }

    [Theory]
    [InlineData(36, 36)]
    [InlineData(45, 36)]
    [InlineData(54, 45)]
    public void MayGrow_ToANotLargerSize_IsRefused(int current, int target)
    {
        BackpackRules.MayGrow(current, target).Should().BeFalse();
    }

    [Theory]
    [InlineData(54, 63)]
    [InlineData(36, 55)]
    [InlineData(36, 255)]
    public void MayGrow_AboveTheMaximum_IsRefused(int current, int target)
    {
        BackpackRules.MayGrow(current, target).Should().BeFalse();
    }
}
