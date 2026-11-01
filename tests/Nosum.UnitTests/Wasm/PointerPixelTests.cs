// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Input;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class PointerPixelTests
{
    [Theory]
    [InlineData(466.138671875, 466)]
    [InlineData(466.5, 467)]
    [InlineData(-2.5, -3)]
    [InlineData(0, 0)]
    [InlineData(1279.99, 1280)]
    public void Of_FractionalCssPixel_RoundsToTheWholePixel(double css, int expected)
    {
        PointerPixel.Of(css).Should().Be(expected);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Of_NotFinite_IsZero(double css)
    {
        PointerPixel.Of(css).Should().Be(0);
    }

    [Fact]
    public void Of_BeyondTheIntRange_ClampsInsteadOfOverflowing()
    {
        PointerPixel.Of(1e12).Should().Be(int.MaxValue);
        PointerPixel.Of(-1e12).Should().Be(int.MinValue);
    }
}
