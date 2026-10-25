// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Core;

/// <summary>Properties of a tile that gameplay reads often, precomputed from its tileset entry.</summary>
[Flags]
public enum TileTraits : byte
{
    /// <summary>No known flag.</summary>
    None = 0,

    /// <summary>The tile is water (<c>Water</c> property true).</summary>
    Water = 1,

    /// <summary>The tile explicitly allows walking (<c>Passable</c> property true).</summary>
    Passable = 2,

    /// <summary>The tile explicitly forbids walking (<c>Passable</c> property false).</summary>
    Impassable = 4,

    /// <summary>The tile can be tilled with a hoe (<c>Diggable</c> property true).</summary>
    Diggable = 8,
}

/// <summary>
/// A tile animation: a looping list of tiles of the same tileset, each shown for some time.
/// </summary>
public sealed class TileAnimation
{
    private readonly int[] _tiles;
    private readonly int[] _ends;

    /// <summary>Initializes an animation.</summary>
    /// <param name="tiles">Local tile id of each frame.</param>
    /// <param name="durationsMs">Duration of each frame, milliseconds (positive).</param>
    public TileAnimation(int[] tiles, int[] durationsMs)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(durationsMs);
        ArgumentOutOfRangeException.ThrowIfZero(tiles.Length);
        ArgumentOutOfRangeException.ThrowIfNotEqual(durationsMs.Length, tiles.Length);

        _tiles = tiles;
        _ends = new int[tiles.Length];
        int total = 0;
        for (int i = 0; i < tiles.Length; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationsMs[i]);
            total += durationsMs[i];
            _ends[i] = total;
        }

        this.TotalMilliseconds = total;
    }

    /// <summary>Gets the number of frames.</summary>
    public int FrameCount => _tiles.Length;

    /// <summary>Gets the length of one loop, milliseconds.</summary>
    public int TotalMilliseconds { get; }

    /// <summary>Returns the local tile id shown at a time; the animation loops. Does not allocate.</summary>
    /// <param name="elapsedMs">Elapsed milliseconds since an arbitrary fixed origin.</param>
    /// <returns>The local tile id of the current frame.</returns>
    public int FrameAt(long elapsedMs)
    {
        long t = elapsedMs % this.TotalMilliseconds;
        if (t < 0)
        {
            t += this.TotalMilliseconds;
        }

        for (int i = 0; i < _ends.Length; i++)
        {
            if (t < _ends[i])
            {
                return _tiles[i];
            }
        }

        return _tiles[^1];
    }
}

/// <summary>
/// Per-tile data of one tileset: precomputed flags, animations and the full property lookup.
/// Indexed by local tile id (global id minus the tileset's first id).
/// </summary>
public sealed class TileProperties
{
    private readonly TileTraits[] _flags;
    private readonly TileAnimation?[] _animations;
    private readonly Dictionary<int, KeyValuePair<string, string>[]> _properties;

    /// <summary>Initializes the data for a tileset.</summary>
    /// <param name="tileCount">Tiles in the tileset.</param>
    /// <param name="properties">Properties by local tile id; may be empty.</param>
    /// <param name="animations">Animations by local tile id; may be empty.</param>
    public TileProperties(
        int tileCount,
        IReadOnlyDictionary<int, KeyValuePair<string, string>[]> properties,
        IReadOnlyDictionary<int, TileAnimation> animations)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(animations);

        _flags = new TileTraits[tileCount];
        _animations = new TileAnimation?[tileCount];
        _properties = new Dictionary<int, KeyValuePair<string, string>[]>(properties.Count);
        foreach ((int id, KeyValuePair<string, string>[] list) in properties)
        {
            _properties[id] = list;
            if ((uint)id < (uint)tileCount)
            {
                _flags[id] = ComputeFlags(list);
            }
        }

        foreach ((int id, TileAnimation animation) in animations)
        {
            if ((uint)id < (uint)tileCount)
            {
                _animations[id] = animation;
            }
        }
    }

    /// <summary>Returns the flags of a tile; unknown tiles have none.</summary>
    /// <param name="localId">Local tile id.</param>
    /// <returns>The flags.</returns>
    public TileTraits GetTraits(int localId) => (uint)localId < (uint)_flags.Length ? _flags[localId] : TileTraits.None;

    /// <summary>Returns a tile's animation, or <see langword="null"/>.</summary>
    /// <param name="localId">Local tile id.</param>
    /// <returns>The animation or <see langword="null"/>.</returns>
    public TileAnimation? GetAnimation(int localId) => (uint)localId < (uint)_animations.Length ? _animations[localId] : null;

    /// <summary>Looks up any property of a tile by name (ordinal).</summary>
    /// <param name="localId">Local tile id.</param>
    /// <param name="name">Property name.</param>
    /// <param name="value">The value as written in the map.</param>
    /// <returns><see langword="true"/> when the tile has the property.</returns>
    public bool TryGetProperty(int localId, string name, out string value)
    {
        if (_properties.TryGetValue(localId, out KeyValuePair<string, string>[]? list))
        {
            foreach (KeyValuePair<string, string> pair in list)
            {
                if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static TileTraits ComputeFlags(KeyValuePair<string, string>[] list)
    {
        TileTraits flags = TileTraits.None;
        foreach (KeyValuePair<string, string> pair in list)
        {
            if (string.Equals(pair.Key, "Water", StringComparison.Ordinal) && IsTrue(pair.Value))
            {
                flags |= TileTraits.Water;
            }
            else if (string.Equals(pair.Key, "Passable", StringComparison.Ordinal))
            {
                flags |= IsTrue(pair.Value) ? TileTraits.Passable : TileTraits.Impassable;
            }
            else if (string.Equals(pair.Key, "Diggable", StringComparison.Ordinal) && IsTrue(pair.Value))
            {
                flags |= TileTraits.Diggable;
            }
        }

        return flags;
    }

    // Tiled "bool" properties write true/false; the placeholder content uses "T"/"F".
    private static bool IsTrue(string value)
        => string.Equals(value, "T", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
