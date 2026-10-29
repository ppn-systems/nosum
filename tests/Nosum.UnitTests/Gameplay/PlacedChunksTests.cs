// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class PlacedChunksTests
{
    // Regression for #156: a piece that crosses a chunk border belongs to every chunk it overlaps, not only to the chunk of its top-left tile.
    [Fact]
    public void RangeOf_ATwoByTwoOnTheCornerOfFourChunks_OverlapsAllFour()
    {
        // The table's top-left tile is the last tile of chunk (0, 0): its footprint reaches into the chunks to the right, below and diagonal.
        PlacedItem table = new(1, ItemCatalog.OakTable, 15, 15, 0);

        ChunkRange range = PlacedChunks.RangeOf(table);

        range.Should().Be(new ChunkRange(new ChunkCoord(0, 0), new ChunkCoord(1, 1)));
        range.Chunks().Should().Equal(new ChunkCoord(0, 0), new ChunkCoord(1, 0), new ChunkCoord(0, 1), new ChunkCoord(1, 1));
    }

    [Fact]
    public void RangeOf_AFootprintInsideOneChunk_IsThatChunkOnly()
    {
        PlacedItem table = new(1, ItemCatalog.OakTable, 2, 2, 0);

        PlacedChunks.RangeOf(table).Should().Be(new ChunkRange(new ChunkCoord(0, 0), new ChunkCoord(0, 0)));
    }

    [Fact]
    public void RangeOf_AOneTilePieceOnAChunkBorder_IsInItsOwnChunkOnly()
    {
        PlacedItem chair = new(2, ItemCatalog.OakChair, 15, 15, 0);

        PlacedChunks.RangeOf(chair).Should().Be(new ChunkRange(new ChunkCoord(0, 0), new ChunkCoord(0, 0)));
    }

    // The footprint follows the rotation: the couch is three tiles wide facing down and two by two facing right.
    [Fact]
    public void RangeOf_ARotatedPiece_UsesTheFootprintOfThatRotation()
    {
        PlacedItem facingDown = new(3, ItemCatalog.BlueCouch, 14, 15, 0);
        PlacedItem facingRight = new(4, ItemCatalog.BlueCouch, 14, 15, 1);

        PlacedChunks.RangeOf(facingDown).Should().Be(new ChunkRange(new ChunkCoord(0, 0), new ChunkCoord(1, 0)));
        PlacedChunks.RangeOf(facingRight).Should().Be(new ChunkRange(new ChunkCoord(0, 0), new ChunkCoord(0, 1)));
    }

    [Fact]
    public void Contains_ChunksInsideTheRangeOnly()
    {
        ChunkRange range = new(new ChunkCoord(0, 0), new ChunkCoord(1, 1));

        range.Contains(new ChunkCoord(1, 0)).Should().BeTrue();
        range.Contains(new ChunkCoord(2, 0)).Should().BeFalse();
        range.Contains(new ChunkCoord(0, -1)).Should().BeFalse();
    }
}
