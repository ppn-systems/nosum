// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The walker runs once a frame in the browser, so a step must not allocate (docs/adr/ADR-099 D2; csharp-style "Allocation").</summary>
public sealed class WalkerAllocationTests
{
    // 20 x 10 tiles of 16 px, open ground.
    private static WalkGrid OpenGrid() => new(20, 10, 16, new bool[20 * 10]);

    [Fact]
    public void Step_HeldDiagonalAndIdle_AllocatesNothing()
    {
        WalkGrid grid = OpenGrid();
        Walker walker = new(80, 80);
        InputState diagonal = InputState.Right | InputState.Down;
        for (int i = 0; i < 10; i++)
        {
            walker.Step(diagonal, 1.0 / 60, grid, 1.5);
            walker.Step(InputState.None, 1.0 / 60, grid);
        }

        long smallest = long.MaxValue;
        for (int window = 0; window < 10 && smallest != 0; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                walker.Step(diagonal, 1.0 / 60, grid, 1.5);
                walker.Step(InputState.Left | InputState.Up, 1.0 / 60, grid);
                walker.Step(InputState.None, 1.0 / 60, grid);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        smallest.Should().Be(0);
    }

    [Fact]
    public void Step_RightHeld_MovesRightAndFacesRight()
    {
        WalkGrid grid = OpenGrid();
        Walker walker = new(80, 80);

        walker.Step(InputState.Right, 0.1, grid);

        walker.X.Should().BeGreaterThan(80);
        walker.Y.Should().Be(80);
        walker.IsMoving.Should().BeTrue();
    }
}
