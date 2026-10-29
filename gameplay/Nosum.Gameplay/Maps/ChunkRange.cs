// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.Gameplay.Maps;

/// <summary>An inclusive rectangle of chunks: the chunks that a thing spanning several tiles overlaps (docs/adr/ADR-022).</summary>
/// <param name="First">The chunk of the top-left tile.</param>
/// <param name="Last">The chunk of the bottom-right tile.</param>
public readonly record struct ChunkRange(ChunkCoord First, ChunkCoord Last)
{
    /// <summary>Tells whether a chunk lies in the range.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <returns><see langword="true"/> when the chunk is one of the range's chunks.</returns>
    public bool Contains(ChunkCoord chunk) => chunk.X >= this.First.X && chunk.X <= this.Last.X && chunk.Y >= this.First.Y && chunk.Y <= this.Last.Y;

    /// <summary>Lists every chunk of the range, row by row.</summary>
    /// <returns>The chunks.</returns>
    public IEnumerable<ChunkCoord> Chunks()
    {
        for (int y = this.First.Y; y <= this.Last.Y; y++)
        {
            for (int x = this.First.X; x <= this.Last.X; x++)
            {
                yield return new ChunkCoord(x, y);
            }
        }
    }
}
