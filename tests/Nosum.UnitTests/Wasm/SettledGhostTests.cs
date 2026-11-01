// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Placement;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class SettledGhostTests
{
    [Fact]
    public void Hide_IsFalse_BeforeAnyPieceWasPlaced()
    {
        SettledGhost settled = default;

        settled.Hide(4, 7).Should().BeFalse();
    }

    [Fact]
    public void Hide_IsTrue_OnTheTileJustPlaced_SoTheGhostDoesNotSitOnThePiece()
    {
        SettledGhost settled = default;
        settled.Settle(4, 7);

        settled.Hide(4, 7).Should().BeTrue();
        settled.Hide(4, 7).Should().BeTrue("the ghost stays hidden while the farmer stands on that tile");
    }

    [Fact]
    public void Hide_ReturnsFalseAndResets_WhenTheGhostMovesToAnotherTile()
    {
        SettledGhost settled = default;
        settled.Settle(4, 7);

        settled.Hide(5, 7).Should().BeFalse("aiming at another tile shows the ghost again");
        settled.Hide(4, 7).Should().BeFalse("walking back to the placed tile does not hide it again");
    }

    [Fact]
    public void Settle_ReplacesAnEarlierTile()
    {
        SettledGhost settled = default;
        settled.Settle(1, 1);
        settled.Settle(2, 2);

        settled.Hide(1, 1).Should().BeFalse();
        settled.Hide(2, 2).Should().BeFalse("the reset above cleared the second tile too");
        settled.Settle(2, 2);
        settled.Hide(2, 2).Should().BeTrue();
    }
}
