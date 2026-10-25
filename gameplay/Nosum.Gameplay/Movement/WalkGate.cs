// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Gameplay.Movement;

/// <summary>
/// Tiles that stay blocked until a community unlock is set (docs/adr/ADR-037 D4): the span of a broken bridge. The grid keeps them blocked in its flags and a
/// gate releases them while <see cref="IsUnlocked"/> says the unlock is set, so every copy of the grid (the node and placement layers clone it) follows the unlock
/// without being rebuilt. The server answers from its unlock set, the client from what the server last told it.
/// </summary>
public sealed class WalkGate
{
    private readonly int[] _tiles;

    /// <summary>Initializes a gate.</summary>
    /// <param name="key">The unlock key (<c>CommunityUnlockKeys</c>) that opens it.</param>
    /// <param name="tiles">The tiles it covers, as map tile coordinates.</param>
    /// <param name="width">Width of the grid the gate is attached to, tiles.</param>
    /// <param name="unlocked">Whether a key is unlocked; <see langword="null"/> keeps the gate shut.</param>
    public WalkGate(string key, IReadOnlyList<(int X, int Y)> tiles, int width, Func<string, bool>? unlocked)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(tiles);

        this.Key = key;
        this.IsUnlocked = unlocked;
        _tiles = new int[tiles.Count];
        for (int i = 0; i < _tiles.Length; i++)
        {
            _tiles[i] = (tiles[i].Y * width) + tiles[i].X;
        }
    }

    /// <summary>Gets the unlock key that opens the gate.</summary>
    public string Key { get; }

    /// <summary>Gets the question the gate asks, or <see langword="null"/> when nothing can open it.</summary>
    public Func<string, bool>? IsUnlocked { get; }

    /// <summary>Returns whether a tile index is released: it is one of the gate's tiles and the unlock is set.</summary>
    /// <param name="index">Row-major tile index.</param>
    /// <returns><see langword="true"/> when the tile may be walked on although the grid flags it as blocked.</returns>
    public bool Releases(int index)
    {
        for (int i = 0; i < _tiles.Length; i++)
        {
            if (_tiles[i] == index)
            {
                return this.IsUnlocked?.Invoke(this.Key) == true;
            }
        }

        return false;
    }
}
