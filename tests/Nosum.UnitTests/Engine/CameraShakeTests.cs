// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>Tests for the combat camera shake (docs/adr/ADR-072).</summary>
public sealed class CameraShakeTests
{
    [Fact]
    public void Offset_BeforeImpact_IsZero()
    {
        CameraShake.Offset(-1, 140, 3, out int dx, out int dy);

        (dx, dy).Should().Be((0, 0));
    }

    [Fact]
    public void Offset_AfterDuration_IsZero()
    {
        CameraShake.Offset(140, 140, 3, out int dx, out int dy);

        (dx, dy).Should().Be((0, 0));
    }

    [Fact]
    public void Offset_ZeroAmplitude_IsZero()
    {
        CameraShake.Offset(10, 140, 0, out int dx, out int dy);

        (dx, dy).Should().Be((0, 0));
    }

    [Fact]
    public void Offset_ThroughoutDuration_StaysWithinAmplitude()
    {
        for (int t = 0; t < 140; t += 3)
        {
            CameraShake.Offset(t, 140, 3, out int dx, out int dy);

            dx.Should().BeInRange(-3, 3);
            dy.Should().BeInRange(-3, 3);
        }
    }

    [Fact]
    public void Offset_NearEnd_DecaysToZero()
    {
        // Strength is amplitude times the share of the duration left: one millisecond from the end it rounds to nothing.
        CameraShake.Offset(139, 140, 3, out int dx, out int dy);

        (dx, dy).Should().Be((0, 0));
    }

    [Fact]
    public void Offset_SameInputs_GivesSameOffset()
    {
        CameraShake.Offset(40, 220, 3, out int firstX, out int firstY);
        CameraShake.Offset(40, 220, 3, out int secondX, out int secondY);

        (secondX, secondY).Should().Be((firstX, firstY));
    }

    [Fact]
    public void Nudge_AfterFollow_OffsetsTheView()
    {
        Camera2D camera = default;
        camera.Follow(100, 100, 50, 50, 1000, 1000);

        camera.Nudge(2, -3);

        (camera.Left, camera.Top).Should().Be((77, 72));
    }
}
