// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// A chunk: a 16 × 16 tile square of one map instance, addressed <c>(tileX >> 4, tileY >> 4)</c> (docs/adr/ADR-022 D1).
/// Chunks are the unit of interest management: who receives a snapshot entry and which world events reach whom.
/// </summary>
/// <param name="X">Chunk column.</param>
/// <param name="Y">Chunk row.</param>
public readonly record struct ChunkCoord(int X, int Y)
{
    /// <summary>The side of a chunk in tiles.</summary>
    public const int SizeTiles = 16;

    /// <summary>The shift that turns a tile coordinate into a chunk coordinate.</summary>
    public const int Shift = 4;

    /// <summary>Returns the chunk that holds a tile.</summary>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns>The chunk.</returns>
    public static ChunkCoord OfTile(int tileX, int tileY) => new(tileX >> Shift, tileY >> Shift);

    /// <summary>Returns the chunk that holds a point in world pixels.</summary>
    /// <param name="x">World x, pixels.</param>
    /// <param name="y">World y, pixels.</param>
    /// <param name="tileSize">Side of a tile in pixels.</param>
    /// <returns>The chunk.</returns>
    public static ChunkCoord OfPixels(double x, double y, int tileSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tileSize, 0);
        return OfTile((int)Math.Floor(x / tileSize), (int)Math.Floor(y / tileSize));
    }

    /// <summary>Gets the chebyshev distance to another chunk: the number of chunk steps, diagonals included.</summary>
    /// <param name="other">The other chunk.</param>
    /// <returns>Distance in chunks.</returns>
    public int DistanceTo(ChunkCoord other) => Math.Max(Math.Abs(this.X - other.X), Math.Abs(this.Y - other.Y));

    /// <summary>Gets the x of the chunk's first tile.</summary>
    public int FirstTileX => this.X << Shift;

    /// <summary>Gets the y of the chunk's first tile.</summary>
    public int FirstTileY => this.Y << Shift;

    /// <summary>Gets a single sortable number for the chunk, row-major, usable as a dictionary or sort key.</summary>
    public long SortKey => ((long)this.Y << 32) | (uint)this.X;

    /// <inheritdoc/>
    public override string ToString() => "(" + this.X + "," + this.Y + ")";
}
