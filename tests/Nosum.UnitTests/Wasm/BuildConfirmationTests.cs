// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Placement;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class BuildConfirmationTests
{
    [Fact]
    public void Press_FirstPressOnABuildingOnlyArms_SecondOnTheSameTileBuilds()
    {
        BuildConfirmation confirm = default;

        confirm.Press(4, 5, 10).Should().BeFalse("the first press asks for confirmation");
        confirm.IsArmed(10.5).Should().BeTrue();
        confirm.Press(4, 5, 11).Should().BeTrue("the second press on the same tile builds");
        confirm.IsArmed(11.5).Should().BeFalse("a build clears the confirmation");
    }

    [Fact]
    public void Press_MovingTheGhostToAnotherTile_ArmsAgainInsteadOfBuilding()
    {
        BuildConfirmation confirm = default;
        confirm.Press(4, 5, 10);

        confirm.Press(6, 5, 11).Should().BeFalse("the confirmation belongs to the tile it was asked for");
        confirm.Press(6, 5, 12).Should().BeTrue();
    }

    [Fact]
    public void Press_AfterTheWindow_NeedsTheFirstPressAgain()
    {
        BuildConfirmation confirm = default;
        confirm.Press(1, 1, 0);

        double late = BuildConfirmation.WindowSeconds + 1;
        confirm.IsArmed(late).Should().BeFalse();
        confirm.Press(1, 1, late).Should().BeFalse("an expired first press does not count");
    }

    [Fact]
    public void Disarm_DropsThePendingConfirmation()
    {
        BuildConfirmation confirm = default;
        confirm.Press(2, 2, 0);

        confirm.Disarm();

        confirm.IsArmed(0.1).Should().BeFalse();
        confirm.Press(2, 2, 0.2).Should().BeFalse();
    }
}
