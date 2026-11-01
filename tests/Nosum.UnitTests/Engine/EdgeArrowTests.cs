// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class EdgeArrowTests
{
    private const int ViewWidth = 320;
    private const int ViewHeight = 180;
    private const int Inset = 24;

    [Fact]
    public void TryPlace_TargetOnScreen_PlacesNothing()
    {
        EdgeArrow.TryPlace(160, 90, ViewWidth, ViewHeight, Inset, out _).Should().BeFalse();
        EdgeArrow.TryPlace(10, 10, ViewWidth, ViewHeight, Inset, out _).Should().BeTrue("a target in the margin the arrow keeps clear still gets its arrow");
    }

    [Fact]
    public void TryPlace_TargetLeftOfTheView_PointsLeftOnTheLeftEdge()
    {
        EdgeArrow.TryPlace(-100, 90, ViewWidth, ViewHeight, Inset, out EdgeArrowPlacement placement).Should().BeTrue();

        placement.Direction.Should().Be(EdgeArrowDirection.Left);
        placement.X.Should().Be(Inset - (EdgeArrow.Size / 2));
        placement.Y.Should().Be(90 - (EdgeArrow.Size / 2));
    }

    [Fact]
    public void TryPlace_TargetAbove_PointsUpOnTheTopEdge()
    {
        EdgeArrow.TryPlace(160, -50, ViewWidth, ViewHeight, Inset, out EdgeArrowPlacement placement).Should().BeTrue();

        placement.Direction.Should().Be(EdgeArrowDirection.Up);
        placement.Y.Should().Be(Inset - (EdgeArrow.Size / 2));
    }

    [Fact]
    public void TryPlace_TargetDiagonal_PointsAlongTheEdgeItReachesFirst()
    {
        // From the centre the ray reaches the bottom inset before the right one, so the arrow sits on the bottom edge and points down.
        EdgeArrow.TryPlace(400, 300, ViewWidth, ViewHeight, Inset, out EdgeArrowPlacement placement).Should().BeTrue();

        placement.Direction.Should().Be(EdgeArrowDirection.Down);
        placement.Y.Should().BeGreaterThan(ViewHeight / 2);
    }

    [Fact]
    public void TryPlace_AnyTargetOffScreen_KeepsTheWholeArrowInsideTheView()
    {
        for (int x = -400; x <= 720; x += 37)
        {
            for (int y = -300; y <= 480; y += 29)
            {
                if (EdgeArrow.TryPlace(x, y, ViewWidth, ViewHeight, Inset, out EdgeArrowPlacement placement))
                {
                    placement.X.Should().BeGreaterThanOrEqualTo(0, "target " + x + "," + y);
                    placement.Y.Should().BeGreaterThanOrEqualTo(0, "target " + x + "," + y);
                    (placement.X + EdgeArrow.Size).Should().BeLessThanOrEqualTo(ViewWidth, "target " + x + "," + y);
                    (placement.Y + EdgeArrow.Size).Should().BeLessThanOrEqualTo(ViewHeight, "target " + x + "," + y);
                }
            }
        }
    }

    [Fact]
    public void TryPlace_ViewSmallerThanTheInset_PlacesNothing()
    {
        EdgeArrow.TryPlace(-500, 0, 40, 40, Inset, out _).Should().BeFalse();
    }
}
