// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Regression tests for issue #166: monsters never cut wall corners and never stack on one tile.</summary>
public sealed class MonsterStepRulesTests
{
    private static WalkGrid Open() => new(10, 10, 16, new bool[100]);

    [Fact]
    public void CanStep_DiagonalPastWallCorner_IsRefused()
    {
        // Walls at (5,4) and (4,5) meet at a vertex; a monster on (4,4) must not slip to (5,5).
        bool[] blocked = new bool[100];
        blocked[(4 * 10) + 5] = true;
        blocked[(5 * 10) + 4] = true;
        WalkGrid grid = new(10, 10, 16, blocked);

        MonsterStepRules.CanStep(grid, 4, 4, 5, 5).Should().BeFalse();
    }

    [Fact]
    public void CanStep_OpenDiagonal_IsAllowed()
    {
        MonsterStepRules.CanStep(Open(), 4, 4, 5, 5).Should().BeTrue();
    }

    [Fact]
    public void CanStep_IntoBlockedTile_IsRefused()
    {
        bool[] blocked = new bool[100];
        blocked[(4 * 10) + 5] = true;

        MonsterStepRules.CanStep(new WalkGrid(10, 10, 16, blocked), 4, 4, 5, 4).Should().BeFalse();
    }

    [Fact]
    public void TryStepToward_DiagonalOccupiedByOtherMonster_StepsAlongAnAxisInstead()
    {
        List<MonsterSpawn> monsters =
        [
            new(1, MonsterKind.CaveBat, 4, 4, 10, 10),
            new(2, MonsterKind.CaveBat, 5, 5, 10, 10),
        ];

        bool moved = MonsterStepRules.TryStepToward(Open(), monsters, 1, 4, 4, 8, 8, out int x, out int y);

        moved.Should().BeTrue();
        (x, y).Should().Be((5, 4), "the diagonal tile is held by monster 2, so the horizontal step is taken");
    }

    [Fact]
    public void TryStepToward_OnlyStepIsOccupied_DoesNotMove()
    {
        List<MonsterSpawn> monsters =
        [
            new(1, MonsterKind.CaveBat, 4, 4, 10, 10),
            new(2, MonsterKind.CaveBat, 5, 4, 10, 10),
        ];

        bool moved = MonsterStepRules.TryStepToward(Open(), monsters, 1, 4, 4, 8, 4, out int x, out int y);

        moved.Should().BeFalse();
        (x, y).Should().Be((4, 4));
    }

    [Fact]
    public void IsOccupied_DeadMonsterDoesNotBlockAndSelfDoesNotBlock()
    {
        List<MonsterSpawn> monsters =
        [
            new(1, MonsterKind.CaveBat, 3, 3, 10, 10),
            new(2, MonsterKind.CaveBat, 6, 6, 0, 10),
        ];

        MonsterStepRules.IsOccupied(monsters, 1, 3, 3).Should().BeFalse("a monster does not block its own tile");
        MonsterStepRules.IsOccupied(monsters, 3, 6, 6).Should().BeFalse("a dead monster does not block");
    }
}
