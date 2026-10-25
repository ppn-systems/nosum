// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Maps;

/// <summary>The chunks a map actually has: a rectangle of whole chunks that covers its tiles. A chunk outside it never holds anything.</summary>
/// <param name="CountX">Number of chunk columns.</param>
/// <param name="CountY">Number of chunk rows.</param>
public readonly record struct ChunkBounds(int CountX, int CountY)
{
    /// <summary>Returns the bounds of a map of a given size.</summary>
    /// <param name="widthTiles">Map width in tiles.</param>
    /// <param name="heightTiles">Map height in tiles.</param>
    /// <returns>The chunk rectangle that covers the map.</returns>
    public static ChunkBounds ForTiles(int widthTiles, int heightTiles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(widthTiles);
        ArgumentOutOfRangeException.ThrowIfNegative(heightTiles);
        return new ChunkBounds(
            (widthTiles + ChunkCoord.SizeTiles - 1) >> ChunkCoord.Shift,
            (heightTiles + ChunkCoord.SizeTiles - 1) >> ChunkCoord.Shift);
    }

    /// <summary>Tells whether a chunk lies inside the map.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <returns><see langword="true"/> when the map has this chunk.</returns>
    public bool Contains(ChunkCoord chunk) => (uint)chunk.X < (uint)this.CountX && (uint)chunk.Y < (uint)this.CountY;

    /// <summary>Moves a chunk to the nearest chunk inside the map, so a position slightly outside still has an owner.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <returns>The chunk itself when inside, otherwise the nearest one.</returns>
    public ChunkCoord Clamp(ChunkCoord chunk) => new(
        Math.Clamp(chunk.X, 0, Math.Max(0, this.CountX - 1)),
        Math.Clamp(chunk.Y, 0, Math.Max(0, this.CountY - 1)));
}
