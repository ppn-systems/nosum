// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Running (docs/adr/ADR-085): the run speed is validated by the same speed limit as a buff, and running costs energy by the second.</summary>
public sealed class RunMovementTests
{
    // 20 x 10 tiles of 16 px (320 x 160 px), open ground.
    private static WalkGrid OpenGrid() => new(20, 10, 16, new bool[20 * 10]);

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 1.5)]
    [InlineData(1.25, true, MovementSpeed.MaxFactor)] // 1.875 before the shared cap (docs/adr/ADR-084 D3)
    [InlineData(1.2, true, 1.8)] // winged boots while running reach the cap exactly (docs/adr/ADR-084 amendment)
    [InlineData(1.2, false, 1.2)]
    [InlineData(1.5, false, 1.5)]
    public void SpeedMultiplier_RunsOnTopOfBuffsUpToTheCap(double buff, bool running, double expected)
        => RunRules.SpeedMultiplier(buff, running).Should().BeApproximately(expected, 1e-9);

    [Fact]
    public void TakeEnergy_OneUnitEveryFourRunningSeconds()
    {
        int debt = 0;
        int spent = 0;

        for (int i = 0; i < 7; i++)
        {
            spent += RunRules.TakeEnergy(ref debt, RunRules.BaseCostMilli(0.5));
        }

        spent.Should().Be(0, "3.5 seconds of running cost nothing yet");
        RunRules.TakeEnergy(ref debt, RunRules.BaseCostMilli(0.5)).Should().Be(1, "four seconds of running cost one energy");
    }

    [Fact]
    public void TakeEnergy_LongIdleGap_ChargesOnlyTheRunningWindow()
    {
        // A player who stood still for half a minute and then starts running owes for the window of the move, not for the idle time.
        int debt = 0;

        RunRules.BaseCostMilli(30).Should().Be(RunRules.BaseCostMilli(RunRules.MaxChargedSeconds));
        RunRules.TakeEnergy(ref debt, RunRules.BaseCostMilli(30)).Should().Be(0);
        debt.Should().Be(RunRules.BaseCostMilli(RunRules.MaxChargedSeconds));
    }

    [Fact]
    public void TakeEnergy_SplitTimeSpendsTheSameAsOneLongRun()
    {
        int whole = 0;
        int split = 0;

        int wholeSpent = 0;
        for (int i = 0; i < 80; i++)
        {
            wholeSpent += RunRules.TakeEnergy(ref whole, RunRules.BaseCostMilli(0.5));
        }

        int splitSpent = 0;
        for (int i = 0; i < 400; i++)
        {
            splitSpent += RunRules.TakeEnergy(ref split, RunRules.BaseCostMilli(0.1));
        }

        wholeSpent.Should().Be(10);
        splitSpent.Should().Be(wholeSpent, "milli-units carry over exactly, so the cost does not depend on how the time is split");
    }

    [Fact]
    public void TakeEnergy_NegativeTimeCostsNothing()
    {
        int debt = 0;

        RunRules.BaseCostMilli(-5).Should().Be(0);
        RunRules.TakeEnergy(ref debt, RunRules.BaseCostMilli(-5)).Should().Be(0);
        debt.Should().Be(0);
    }

    [Theory]
    [InlineData(ItemCatalog.LeatherBoots, 5)]
    [InlineData(ItemCatalog.SwiftBoots, 4)]
    [InlineData(ItemCatalog.WingedBoots, 3)]
    [InlineData(0, 5)]
    public void RunEnergy_TwentySecondsOfRunningCostsLessInBetterBoots(int feetItemId, int expectedUnits)
    {
        // Forty 0.5 s calls: the base cost is 5 energy; the boots take their share off each call before the whole units are spent (docs/adr/ADR-084 D5).
        int debt = 0;
        int spent = 0;

        for (int i = 0; i < 40; i++)
        {
            int costMilli = FootwearRules.RunEnergyCost(RunRules.BaseCostMilli(0.5), feetItemId);
            spent += RunRules.TakeEnergy(ref debt, costMilli);
        }

        spent.Should().Be(expectedUnits);
    }

    [Fact]
    public void Validate_RunClaimWithTheRunFactor_IsAccepted()
    {
        // Ten 100 ms steps at run pace: a run is the same kind of sustained speed a Swiftness buff is.
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        double pace = Walker.Speed * RunRules.SpeedFactor * 0.1;

        for (int i = 1; i <= 10; i++)
        {
            MoveVerdict verdict = MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid, RunRules.SpeedMultiplier(1, running: true));
            verdict.Should().Be(MoveVerdict.Accepted, $"step {i}");
        }
    }

    [Fact]
    public void Validate_RunClaimWithoutTheRunFactor_IsRefusedOnceTheBudgetRunsOut()
    {
        // The server grants the run speed only while the player has energy: a run claim it does not grant is walking speed, and the run pace is refused.
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        double pace = Walker.Speed * RunRules.SpeedFactor * 0.1;
        bool refused = false;

        for (int i = 1; i <= 10 && !refused; i++)
        {
            MoveVerdict verdict = MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid, RunRules.SpeedMultiplier(1, running: false));
            refused = verdict == MoveVerdict.TooFast;
        }

        refused.Should().BeTrue("walking speed cannot sustain the run pace");
    }

    [Fact]
    public void Validate_RunningFasterThanTheRunFactor_IsRefusedOnceTheBudgetRunsOut()
    {
        // Double walking pace is faster than a run (1.5x): sustained, it outruns the budget even with the run factor (refused at about step 11).
        MotionState state = new(20, 80, 0);
        WalkGrid grid = OpenGrid();
        double pace = Walker.Speed * 2 * 0.1;
        bool refused = false;

        for (int i = 1; i <= 15 && !refused; i++)
        {
            MoveVerdict verdict = MoveValidator.Validate(ref state, 20 + (pace * i), 80, 1, true, i * 0.1, grid, RunRules.SpeedMultiplier(1, running: true));
            refused = verdict == MoveVerdict.TooFast;
        }

        refused.Should().BeTrue();
    }

    [Fact]
    public void Walker_RunningStep_CoversTheRunDistance()
    {
        Walker normal = new(20, 80);
        Walker runner = new(20, 80);

        normal.Step(InputState.Right, 0.25, OpenGrid());
        runner.Step(InputState.Right | InputState.Run, 0.25, OpenGrid(), RunRules.SpeedMultiplier(1, running: true));

        (runner.X - 20).Should().BeApproximately((normal.X - 20) * RunRules.SpeedFactor, 1e-9);
    }
}
