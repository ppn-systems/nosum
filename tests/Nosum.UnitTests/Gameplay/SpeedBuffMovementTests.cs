// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Regression tests for issue #146: a speed buff must raise what both the validator and the client walker allow.</summary>
public sealed class SpeedBuffMovementTests
{
    // 20 x 10 tiles of 16 px (320 x 160 px), open ground.
    private static WalkGrid OpenGrid() => new(20, 10, 16, new bool[20 * 10]);

    [Fact]
    public void Validate_JumpBeyondWalkingBudgetWithoutBuff_IsTooFast()
    {
        MotionState state = new(20, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 65, 80, 1, true, 0.1, OpenGrid());

        verdict.Should().Be(MoveVerdict.TooFast);
        state.X.Should().Be(20);
    }

    [Fact]
    public void Validate_SameJumpWithSwiftnessFactor_IsAccepted()
    {
        MotionState state = new(20, 80, 0);

        MoveVerdict verdict = MoveValidator.Validate(ref state, 65, 80, 1, true, 0.1, OpenGrid(), StatusEffectRules.SwiftnessSpeedFactor);

        verdict.Should().Be(MoveVerdict.Accepted);
        state.X.Should().Be(65);
    }

    [Fact]
    public void Validate_SustainedSprintWithSwiftness_KeepsPace()
    {
        // Ten 100 ms steps at Swiftness pace cover 10 px more than normal walking would allow.
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        double pace = Walker.Speed * StatusEffectRules.SwiftnessSpeedFactor * 0.1;

        for (int i = 1; i <= 10; i++)
        {
            MoveVerdict verdict = MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid, StatusEffectRules.SwiftnessSpeedFactor);
            verdict.Should().Be(MoveVerdict.Accepted, $"step {i}");
        }
    }

    [Fact]
    public void Validate_SustainedSprintWithoutBuff_IsRefusedOnceTheBudgetRunsOut()
    {
        // Sprinting at double walking pace outruns the budget: the same run that a Swiftness buff sustains is refused without it.
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        double pace = Walker.Speed * 2 * 0.1;

        MoveVerdict last = MoveVerdict.Accepted;
        for (int i = 1; i <= 10 && last == MoveVerdict.Accepted; i++)
        {
            last = MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid);
        }

        last.Should().Be(MoveVerdict.TooFast);
    }

    [Fact]
    public void Validate_FactorBelowOne_Throws()
    {
        MotionState state = new(20, 80, 0);

        Action act = () => MoveValidator.Validate(ref state, 21, 80, 1, true, 0.1, OpenGrid(), 0.5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Step_WithSwiftnessFactor_WalksFartherInTheSameTime()
    {
        Walker normal = new(20, 80);
        Walker swift = new(20, 80);

        normal.Step(InputState.Right, 0.25, OpenGrid());
        swift.Step(InputState.Right, 0.25, OpenGrid(), StatusEffectRules.SwiftnessSpeedFactor);

        (swift.X - 20).Should().BeApproximately((normal.X - 20) * StatusEffectRules.SwiftnessSpeedFactor, 1e-9);
    }
}
