// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// The set of chunks one player is interested in (docs/adr/ADR-022 D2): the player's own chunk and its 8 neighbours, plus one chunk of
/// hysteresis. A chunk joins the set when it is within <see cref="EnterRadius"/> chunks of the player and leaves it only when it is farther than
/// <see cref="KeepRadius"/>, so walking back and forth over a chunk border adds and drops nothing. The set is kept as a 5 × 5 bit window around
/// <see cref="Center"/>, so the whole class never allocates after construction. Pure rules: not thread-safe, the owner serializes access.
/// </summary>
public sealed class ChunkInterest
{
    /// <summary>The chebyshev distance within which a chunk joins the set: the 3 × 3 block around the player.</summary>
    public const int EnterRadius = 1;

    /// <summary>The chebyshev distance beyond which a chunk leaves the set: one chunk of hysteresis.</summary>
    public const int KeepRadius = 2;

    /// <summary>The most chunks the set can hold, and so the most one move can add or drop.</summary>
    public const int MaxChunks = 25;

    private const int Window = (2 * KeepRadius) + 1;

    private readonly ChunkBounds _bounds;
    private ChunkCoord _center;
    private uint _mask;

    /// <summary>Creates the set of a player who just arrived: the 3 × 3 block around their chunk.</summary>
    /// <param name="center">The player's chunk.</param>
    /// <param name="bounds">The chunks the map has; chunks outside never join the set.</param>
    public ChunkInterest(ChunkCoord center, ChunkBounds bounds)
    {
        _bounds = bounds;
        _center = center;
        for (int dy = -EnterRadius; dy <= EnterRadius; dy++)
        {
            for (int dx = -EnterRadius; dx <= EnterRadius; dx++)
            {
                ChunkCoord chunk = new(center.X + dx, center.Y + dy);
                if (bounds.Contains(chunk))
                {
                    _mask |= BitOf(center, chunk);
                }
            }
        }
    }

    /// <summary>Gets the chunks the map has; none outside them ever joins the set.</summary>
    public ChunkBounds Bounds => _bounds;

    /// <summary>Gets the chunk the set is centred on: the player's chunk as of the last <see cref="MoveTo"/>.</summary>
    public ChunkCoord Center => _center;

    /// <summary>Gets the number of chunks in the set.</summary>
    public int Count => System.Numerics.BitOperations.PopCount(_mask);

    /// <summary>Tells whether a chunk is in the set.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <returns><see langword="true"/> when the player is interested in it.</returns>
    public bool Contains(ChunkCoord chunk)
    {
        int dx = chunk.X - _center.X;
        int dy = chunk.Y - _center.Y;
        if (dx < -KeepRadius || dx > KeepRadius || dy < -KeepRadius || dy > KeepRadius)
        {
            return false;
        }

        return (_mask & BitAt(dx, dy)) != 0;
    }

    /// <summary>Copies the chunks of the set.</summary>
    /// <param name="into">Receives the chunks; needs room for <see cref="Count"/> (at most <see cref="MaxChunks"/>).</param>
    /// <returns>The number of chunks written.</returns>
    public int CopyTo(Span<ChunkCoord> into)
    {
        int count = 0;
        for (int dy = -KeepRadius; dy <= KeepRadius; dy++)
        {
            for (int dx = -KeepRadius; dx <= KeepRadius; dx++)
            {
                if ((_mask & BitAt(dx, dy)) != 0)
                {
                    into[count++] = new ChunkCoord(_center.X + dx, _center.Y + dy);
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Re-centres the set on the player's new chunk and reports what changed. Chunks within <see cref="EnterRadius"/> of the new chunk join;
    /// chunks already in the set stay while within <see cref="KeepRadius"/>; the rest leave.
    /// </summary>
    /// <param name="to">The player's new chunk.</param>
    /// <param name="entered">Receives the chunks that joined (room for <see cref="MaxChunks"/>).</param>
    /// <param name="enteredCount">The number of chunks written to <paramref name="entered"/>.</param>
    /// <param name="left">Receives the chunks that left (room for <see cref="MaxChunks"/>).</param>
    /// <param name="leftCount">The number of chunks written to <paramref name="left"/>.</param>
    /// <returns><see langword="true"/> when the set changed.</returns>
    public bool MoveTo(ChunkCoord to, Span<ChunkCoord> entered, out int enteredCount, Span<ChunkCoord> left, out int leftCount)
    {
        enteredCount = 0;
        leftCount = 0;
        if (to == _center)
        {
            return false;
        }

        ChunkCoord oldCenter = _center;
        uint oldMask = _mask;
        uint newMask = 0;
        for (int dy = -KeepRadius; dy <= KeepRadius; dy++)
        {
            for (int dx = -KeepRadius; dx <= KeepRadius; dx++)
            {
                ChunkCoord chunk = new(to.X + dx, to.Y + dy);
                if (!_bounds.Contains(chunk))
                {
                    continue;
                }

                bool near = Math.Abs(dx) <= EnterRadius && Math.Abs(dy) <= EnterRadius;
                bool wasIn = ContainsIn(oldCenter, oldMask, chunk);
                if (near || wasIn)
                {
                    newMask |= BitAt(dx, dy);
                    if (!wasIn)
                    {
                        entered[enteredCount++] = chunk;
                    }
                }
            }
        }

        for (int dy = -KeepRadius; dy <= KeepRadius; dy++)
        {
            for (int dx = -KeepRadius; dx <= KeepRadius; dx++)
            {
                if ((oldMask & BitAt(dx, dy)) == 0)
                {
                    continue;
                }

                ChunkCoord chunk = new(oldCenter.X + dx, oldCenter.Y + dy);
                if (!ContainsIn(to, newMask, chunk))
                {
                    left[leftCount++] = chunk;
                }
            }
        }

        _center = to;
        _mask = newMask;
        return enteredCount > 0 || leftCount > 0 || oldMask != newMask;
    }

    private static bool ContainsIn(ChunkCoord center, uint mask, ChunkCoord chunk)
    {
        int dx = chunk.X - center.X;
        int dy = chunk.Y - center.Y;
        return dx >= -KeepRadius && dx <= KeepRadius && dy >= -KeepRadius && dy <= KeepRadius && (mask & BitAt(dx, dy)) != 0;
    }

    private static uint BitOf(ChunkCoord center, ChunkCoord chunk) => BitAt(chunk.X - center.X, chunk.Y - center.Y);

    private static uint BitAt(int dx, int dy) => 1u << (((dy + KeepRadius) * Window) + dx + KeepRadius);
}
