// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class ChunkMathTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(15, 15, 0, 0)]
    [InlineData(16, 0, 1, 0)]
    [InlineData(31, 47, 1, 2)]
    [InlineData(129, 109, 8, 6)]
    public void OfTile_Tile_ReturnsItsChunk(int tileX, int tileY, int chunkX, int chunkY)
    {
        ChunkCoord chunk = ChunkCoord.OfTile(tileX, tileY);

        chunk.Should().Be(new ChunkCoord(chunkX, chunkY));
    }

    [Fact]
    public void OfPixels_FeetOnTheBorder_BelongToTheNextChunk()
    {
        ChunkCoord before = ChunkCoord.OfPixels((16 * 16) - 0.01, 8, 16);
        ChunkCoord on = ChunkCoord.OfPixels(16 * 16, 8, 16);

        before.Should().Be(new ChunkCoord(0, 0));
        on.Should().Be(new ChunkCoord(1, 0));
    }

    [Fact]
    public void OfPixels_SlightlyOutsideTheMap_FloorsToTheNegativeChunk()
    {
        ChunkCoord chunk = ChunkCoord.OfPixels(-0.5, -0.5, 16);

        chunk.Should().Be(new ChunkCoord(-1, -1));
    }

    [Fact]
    public void DistanceTo_Diagonal_IsTheLargerStep()
    {
        new ChunkCoord(2, 2).DistanceTo(new ChunkCoord(5, 3)).Should().Be(3);
    }

    [Fact]
    public void ForTiles_Town_Has9By7Chunks()
    {
        ChunkBounds bounds = ChunkBounds.ForTiles(130, 110);

        bounds.Should().Be(new ChunkBounds(9, 7));
        bounds.Contains(new ChunkCoord(8, 6)).Should().BeTrue();
        bounds.Contains(new ChunkCoord(9, 6)).Should().BeFalse();
        bounds.Contains(new ChunkCoord(-1, 0)).Should().BeFalse();
    }

    [Fact]
    public void ForTiles_ExactMultiple_AddsNoSpareChunk()
    {
        ChunkBounds.ForTiles(32, 16).Should().Be(new ChunkBounds(2, 1));
    }

    [Fact]
    public void Constructor_InTheMiddle_HoldsTheThreeByThreeBlock()
    {
        ChunkInterest interest = new(new ChunkCoord(4, 3), ChunkBounds.ForTiles(130, 110));

        interest.Count.Should().Be(9);
        interest.Contains(new ChunkCoord(3, 2)).Should().BeTrue();
        interest.Contains(new ChunkCoord(5, 4)).Should().BeTrue();
        interest.Contains(new ChunkCoord(6, 3)).Should().BeFalse("two chunks away is outside until the player walks there");
    }

    [Fact]
    public void Constructor_InACorner_HoldsOnlyChunksTheMapHas()
    {
        ChunkInterest interest = new(new ChunkCoord(0, 0), ChunkBounds.ForTiles(130, 110));

        interest.Count.Should().Be(4);
        interest.Contains(new ChunkCoord(-1, 0)).Should().BeFalse();
    }

    [Fact]
    public void MoveTo_OneStepEast_AddsTheNewColumnAndKeepsTheOldOneAsHysteresis()
    {
        ChunkInterest interest = new(new ChunkCoord(4, 3), ChunkBounds.ForTiles(130, 110));
        Span<ChunkCoord> entered = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        Span<ChunkCoord> left = stackalloc ChunkCoord[ChunkInterest.MaxChunks];

        bool changed = interest.MoveTo(new ChunkCoord(5, 3), entered, out int enteredCount, left, out int leftCount);

        changed.Should().BeTrue();
        entered[..enteredCount].ToArray().Should().BeEquivalentTo(new[] { new ChunkCoord(6, 2), new ChunkCoord(6, 3), new ChunkCoord(6, 4) });
        leftCount.Should().Be(0, "the column behind is only two chunks away now, which is kept");
        interest.Contains(new ChunkCoord(3, 3)).Should().BeTrue();
        interest.Count.Should().Be(12);
    }

    [Fact]
    public void MoveTo_BackAcrossTheBorder_ChangesNothing()
    {
        ChunkInterest interest = new(new ChunkCoord(4, 3), ChunkBounds.ForTiles(130, 110));
        Span<ChunkCoord> entered = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        Span<ChunkCoord> left = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        _ = interest.MoveTo(new ChunkCoord(5, 3), entered, out _, left, out _);

        bool changed = interest.MoveTo(new ChunkCoord(4, 3), entered, out int enteredCount, left, out int leftCount);

        changed.Should().BeTrue("the centre moved");
        enteredCount.Should().Be(0);
        leftCount.Should().Be(0, "walking along a border must not flap chunks in and out");
    }

    [Fact]
    public void MoveTo_FarAwayFromTheBorder_DropsTheChunksLeftBehind()
    {
        ChunkInterest interest = new(new ChunkCoord(4, 3), ChunkBounds.ForTiles(130, 110));
        Span<ChunkCoord> entered = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        Span<ChunkCoord> left = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        _ = interest.MoveTo(new ChunkCoord(5, 3), entered, out _, left, out _);

        _ = interest.MoveTo(new ChunkCoord(6, 3), entered, out int enteredCount, left, out int leftCount);

        left[..leftCount].ToArray().Should().BeEquivalentTo(new[] { new ChunkCoord(3, 2), new ChunkCoord(3, 3), new ChunkCoord(3, 4) });
        enteredCount.Should().Be(3);
        interest.Contains(new ChunkCoord(3, 3)).Should().BeFalse();
        interest.Contains(new ChunkCoord(4, 3)).Should().BeTrue("still two chunks away");
    }

    [Fact]
    public void MoveTo_ToTheSameChunk_ReportsNoChange()
    {
        ChunkInterest interest = new(new ChunkCoord(4, 3), ChunkBounds.ForTiles(130, 110));
        Span<ChunkCoord> entered = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        Span<ChunkCoord> left = stackalloc ChunkCoord[ChunkInterest.MaxChunks];

        bool changed = interest.MoveTo(new ChunkCoord(4, 3), entered, out int enteredCount, left, out int leftCount);

        changed.Should().BeFalse();
        (enteredCount + leftCount).Should().Be(0);
    }

    [Fact]
    public void MoveTo_ACrossMapJump_DropsEverythingAndEntersTheNewBlock()
    {
        ChunkInterest interest = new(new ChunkCoord(0, 0), ChunkBounds.ForTiles(130, 110));
        Span<ChunkCoord> entered = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        Span<ChunkCoord> left = stackalloc ChunkCoord[ChunkInterest.MaxChunks];

        _ = interest.MoveTo(new ChunkCoord(7, 5), entered, out int enteredCount, left, out int leftCount);

        leftCount.Should().Be(4);
        enteredCount.Should().Be(9);
        interest.Contains(new ChunkCoord(0, 0)).Should().BeFalse();
    }

    [Fact]
    public void MoveTo_RandomWalk_KeepsEnteredLeftAndContainsConsistent()
    {
        ChunkBounds bounds = ChunkBounds.ForTiles(130, 110);
        ChunkInterest interest = new(new ChunkCoord(4, 3), bounds);
        HashSet<ChunkCoord> mirror = [];
        Span<ChunkCoord> initial = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        int initialCount = interest.CopyTo(initial);
        for (int i = 0; i < initialCount; i++)
        {
            _ = mirror.Add(initial[i]);
        }

        Random random = new(7);
        ChunkCoord at = new(4, 3);
        ChunkCoord[] enteredBuffer = new ChunkCoord[ChunkInterest.MaxChunks];
        ChunkCoord[] leftBuffer = new ChunkCoord[ChunkInterest.MaxChunks];
        for (int step = 0; step < 2000; step++)
        {
            at = bounds.Clamp(new ChunkCoord(at.X + random.Next(-2, 3), at.Y + random.Next(-2, 3)));
            _ = interest.MoveTo(at, enteredBuffer, out int enteredCount, leftBuffer, out int leftCount);
            for (int i = 0; i < leftCount; i++)
            {
                mirror.Remove(leftBuffer[i]).Should().BeTrue("only chunks that were in the set can leave");
            }

            for (int i = 0; i < enteredCount; i++)
            {
                mirror.Add(enteredBuffer[i]).Should().BeTrue("only chunks that were absent can enter");
            }

            interest.Count.Should().Be(mirror.Count);
            interest.Contains(at).Should().BeTrue("the player's own chunk is always interesting");
            foreach (ChunkCoord chunk in mirror)
            {
                interest.Contains(chunk).Should().BeTrue();
                chunk.DistanceTo(at).Should().BeLessThanOrEqualTo(ChunkInterest.KeepRadius);
            }
        }
    }
}
