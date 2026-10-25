// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Gated tiles (docs/adr/ADR-037 D4): blocked in the flags, released while the unlock is set, on the grid and on every copy of it.</summary>
public sealed class WalkGateTests
{
    private static WalkGrid Wall() => new(4, 1, 16, [false, true, true, false]);

    [Fact]
    public void Grid_WithAGateAndNoSource_StaysBlocked()
    {
        WalkGrid grid = Wall();
        grid.AddGate(new WalkGate("bridge", [(1, 0), (2, 0)], 4, null));

        grid.IsBlocked(1, 0).Should().BeTrue();
        grid.IsBlocked(2, 0).Should().BeTrue();
    }

    [Fact]
    public void Grid_AGateOpensOnlyItsOwnTiles_AndOnlyWhileTheKeyIsSet()
    {
        string? asked = null;
        bool open = false;
        WalkGrid grid = new(4, 2, 16, [false, true, true, true, true, true, false, false]);
        grid.AddGate(new WalkGate("bridge", [(1, 0), (2, 0)], 4, key =>
        {
            asked = key;
            return open;
        }));

        grid.IsBlocked(1, 0).Should().BeTrue();
        asked.Should().Be("bridge");
        open = true;
        grid.IsBlocked(1, 0).Should().BeFalse();
        grid.IsBlocked(2, 0).Should().BeFalse();
        grid.IsBlocked(3, 0).Should().BeTrue("not a gated tile");
        grid.IsBlocked(0, 1).Should().BeTrue("not a gated tile");
        grid.IsBlocked(-1, 0).Should().BeTrue("outside the map is never released");
        MoveValidatorProbe.Foot(grid, 24, 15).Should().BeFalse("the feet stand on a released tile");
        open = false;
        grid.IsBlocked(1, 0).Should().BeTrue("the gate asks every time");
    }

    [Fact]
    public void Clone_SharesTheGates_SoTheNodeAndPlacementLayersFollowTheUnlock()
    {
        bool open = false;
        WalkGrid grid = Wall();
        grid.AddGate(new WalkGate("bridge", [(1, 0), (2, 0)], 4, _ => open));
        WalkGrid copy = grid.Clone();
        WalkGrid joined = copy.Clone();
        joined.BlockWhereBlocked(grid);

        open = true;

        copy.IsBlocked(1, 0).Should().BeFalse();
        joined.IsBlocked(2, 0).Should().BeFalse("a grid joined from copies still opens with the unlock");
    }

    [Fact]
    public void Gate_ANullKeyIsRefused()
    {
        Action create = () => _ = new WalkGate(string.Empty, [(0, 0)], 4, null);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UnlockWire_PacksTheBridge_AndReadsItBack()
    {
        CommunityUnlockWire.Pack(_ => false).Should().Be(0);
        CommunityUnlockWire.Pack(key => key == CommunityUnlockKeys.BeachBridge).Should().Be((byte)CommunityUnlockSet.BeachBridge);
        CommunityUnlockWire.Pack(key => key == CommunityUnlockKeys.TownLanterns).Should().Be(0, "only unlocks that change the walk grid travel");

        CommunityUnlockWire.IsSet((byte)CommunityUnlockSet.BeachBridge, CommunityUnlockKeys.BeachBridge).Should().BeTrue();
        CommunityUnlockWire.IsSet(0, CommunityUnlockKeys.BeachBridge).Should().BeFalse();
        CommunityUnlockWire.IsSet(byte.MaxValue, CommunityUnlockKeys.FestivalStall).Should().BeFalse("a key the wire does not carry is never set");
    }

    private static class MoveValidatorProbe
    {
        public static bool Foot(WalkGrid grid, double x, double y) => Nosum.Gameplay.Movement.MoveValidator.FootBlocked(grid, x, y);
    }
}
