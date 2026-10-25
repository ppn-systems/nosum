// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class MoveValidatorTests
{
    // 20 x 10 tiles of 16 px (320 x 160 px); a one-tile-thick wall on column 10.
    private static WalkGrid Grid()
    {
        bool[] blocked = new bool[20 * 10];
        for (int row = 0; row < 10; row++)
        {
            blocked[(row * 20) + 10] = true;
        }

        return new WalkGrid(20, 10, 16, blocked);
    }

    [Fact]
    public void Validate_StepAtWalkingSpeed_IsAcceptedAndApplied()
    {
        MotionState state = new(40, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 48, 80, 1, true, 0.1, Grid());

        verdict.Should().Be(MoveVerdict.Accepted);
        state.X.Should().Be(48);
        state.Facing.Should().Be(1);
        state.Moving.Should().BeTrue();
    }

    [Fact]
    public void Validate_TenStepsOverOneSecond_AreAllAccepted()
    {
        MotionState state = new(20, 80, 0);
        WalkGrid grid = Grid();

        for (int i = 1; i <= 10; i++)
        {
            MoveValidator.Validate(ref state, 20 + (i * 8), 80, 1, true, i * 0.1, grid).Should().Be(MoveVerdict.Accepted);
        }
    }

    [Fact]
    public void Validate_BunchedClaimsAfterJitter_AreAccepted()
    {
        MotionState state = new(20, 80, 0);
        WalkGrid grid = Grid();

        // Two 100 ms steps of walking arrive 10 ms apart.
        MoveValidator.Validate(ref state, 28, 80, 1, true, 0.15, grid).Should().Be(MoveVerdict.Accepted);
        MoveValidator.Validate(ref state, 36, 80, 1, true, 0.16, grid).Should().Be(MoveVerdict.Accepted);
    }

    [Fact]
    public void Validate_TeleportAcrossMap_IsTooFastAndKeepsPosition()
    {
        MotionState state = new(40, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 200, 80, 1, true, 0.1, Grid());

        verdict.Should().Be(MoveVerdict.TooFast);
        state.X.Should().Be(40);
    }

    [Fact]
    public void Validate_SpeedHackSustained_IsRefusedOnceTheBudgetIsSpent()
    {
        MotionState state = new(20, 80, 0);
        WalkGrid grid = Grid();
        int refused = 0;

        // 16 px per 100 ms is twice the walking speed.
        for (int i = 1; i <= 10; i++)
        {
            if (MoveValidator.Validate(ref state, 20 + (i * 16), 80, 1, true, i * 0.1, grid) != MoveVerdict.Accepted)
            {
                refused++;
            }
        }

        refused.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Validate_IdleForLongTime_DoesNotStoreMoreThanTheBudgetCap()
    {
        MotionState state = new(20, 80, 0);

        // 60 s idle would "earn" 4800 px if hoarding were allowed.
        MoveVerdict verdict = MoveValidator.Validate(ref state, 120, 80, 1, true, 60, Grid());

        verdict.Should().Be(MoveVerdict.TooFast);
    }

    [Theory]
    [InlineData(-5, 80)]
    [InlineData(80, -1)]
    [InlineData(320, 80)]
    [InlineData(80, 160)]
    public void Validate_OutsideMap_IsOutOfMap(double x, double y)
    {
        MotionState state = new(x < 0 ? 4 : x >= 320 ? 316 : x, y < 0 ? 4 : y >= 160 ? 156 : y, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, x, y, 0, true, 1, Grid());

        verdict.Should().Be(MoveVerdict.OutOfMap);
    }

    [Fact]
    public void Validate_IntoBlockedTile_IsBlocked()
    {
        MotionState state = new(150, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 156, 80, 1, true, 1, Grid());

        verdict.Should().Be(MoveVerdict.Blocked);
        state.X.Should().Be(150);
    }

    [Fact]
    public void Validate_JumpOverThinWallWithSavedBudget_IsBlocked()
    {
        // Feet at 148 stand left of the wall (160-176); 190 is clear on the right and within budget.
        MotionState state = new(148, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 190, 80, 1, true, 5, Grid());

        verdict.Should().Be(MoveVerdict.Blocked);
    }

    [Theory]
    [InlineData(double.NaN, 80, 0)]
    [InlineData(40, double.PositiveInfinity, 0)]
    [InlineData(40, 80, 4)]
    [InlineData(40, 80, -1)]
    public void Validate_NonFiniteOrUnknownFacing_IsInvalid(double x, double y, int facing)
    {
        MotionState state = new(40, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, x, y, facing, true, 1, Grid());

        verdict.Should().Be(MoveVerdict.Invalid);
    }

    [Fact]
    public void Validate_TimeGoingBackwards_EarnsNothing()
    {
        MotionState state = new(40, 80, 10);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 41, 80, 1, true, 5, Grid());

        verdict.Should().Be(MoveVerdict.Accepted);
        state.LastSeconds.Should().Be(5);
    }
}
