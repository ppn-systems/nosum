// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>The off-screen test of docs/adr/ADR-071 D4: a critter outside the view is skipped, and a rectangle that only touches an edge does not count.</summary>
public sealed class ViewCullingTests
{
    private static Camera2D View()
    {
        var camera = new Camera2D();
        camera.Follow(centerX: 500, centerY: 500, viewWidth: 200, viewHeight: 100, worldWidth: 10_000, worldHeight: 10_000);
        return camera;
    }

    [Fact]
    public void Overlaps_RectangleInsideTheView_IsVisible()
    {
        Camera2D camera = View();

        ViewCulling.Overlaps(camera.Left + 10, camera.Top + 10, 16, 16, camera).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_RectangleFarLeftOfTheView_IsCulled()
    {
        Camera2D camera = View();

        ViewCulling.Overlaps(camera.Left - 100, camera.Top + 10, 16, 16, camera).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_RectangleFarBelowTheView_IsCulled()
    {
        Camera2D camera = View();

        ViewCulling.Overlaps(camera.Left + 10, camera.Top + camera.Height + 50, 16, 16, camera).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_RectangleTouchingTheLeftEdgeFromOutside_IsCulled()
    {
        Camera2D camera = View();

        ViewCulling.Overlaps(camera.Left - 16, camera.Top + 10, 16, 16, camera).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_RectangleCrossingTheRightEdge_IsVisible()
    {
        Camera2D camera = View();

        ViewCulling.Overlaps(camera.Left + camera.Width - 4, camera.Top + 10, 16, 16, camera).Should().BeTrue();
    }
}
