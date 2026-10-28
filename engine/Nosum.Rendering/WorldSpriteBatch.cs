// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>
/// The <c>world</c> layer of docs/adr/ADR-005 D4: sprites collected during a frame, then drawn in
/// ascending anchor y (ties by x, then stable id, then insertion order). Everything is preallocated;
/// sorting is in place and allocates nothing.
/// </summary>
public sealed class WorldSpriteBatch
{
    private readonly Sprite[] _sprites;
    private readonly SortKey[] _keys;
    private int _count;

    /// <summary>Initializes a batch.</summary>
    /// <param name="capacity">Maximum sprites per frame.</param>
    public WorldSpriteBatch(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _sprites = new Sprite[capacity];
        _keys = new SortKey[capacity];
    }

    /// <summary>Gets the number of sprites added since <see cref="Begin"/>.</summary>
    public int Count => _count;

    /// <summary>Starts a frame.</summary>
    public void Begin() => _count = 0;

    /// <summary>
    /// Adds a sprite. Sprites past the capacity are dropped. Several sprites of one character may
    /// share <paramref name="sortY"/>, <paramref name="sortX"/> and <paramref name="id"/>: they then
    /// draw in the order added.
    /// </summary>
    /// <param name="texture">Backend texture id.</param>
    /// <param name="srcX">Source left.</param>
    /// <param name="srcY">Source top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="worldX">Destination left, world pixels.</param>
    /// <param name="worldY">Destination top, world pixels.</param>
    /// <param name="sortY">Anchor y (feet, base), world pixels.</param>
    /// <param name="sortX">Anchor x, world pixels, breaking ties in y.</param>
    /// <param name="id">Stable id breaking remaining ties.</param>
    /// <param name="flags">Draw flags such as <see cref="RenderCommandBuffer.FlipX"/>.</param>
    /// <param name="tint">Multiply tint or <see cref="RenderCommandBuffer.NoTint"/>.</param>
    public void Add(int texture, int srcX, int srcY, int width, int height, int worldX, int worldY, int sortY, int sortX = 0, int id = 0, int flags = 0, int tint = RenderCommandBuffer.NoTint)
    {
        if (_count == _sprites.Length)
        {
            return;
        }

        _sprites[_count] = new Sprite(texture, srcX, srcY, width, height, worldX, worldY, flags, tint);
        _keys[_count] = new SortKey(sortY, sortX, id, _count);
        _count++;
    }

    /// <summary>Sorts and records the sprites that intersect the camera, then empties the batch (unless labels are deferred to <see cref="FlushLabels"/>).</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="deferLabels">When <see langword="true"/>, <see cref="RenderCommandBuffer.LabelTexture"/> entries are held until <see cref="FlushLabels"/> so they draw above front layers and ambient darkness.</param>
    public void Flush(RenderCommandBuffer commands, in Camera2D camera, bool deferLabels = false)
    {
        ArgumentNullException.ThrowIfNull(commands);

        Array.Sort(_keys, 0, _count);
        for (int i = 0; i < _count; i++)
        {
            ref readonly Sprite s = ref _sprites[_keys[i].Index];
            if (deferLabels && s.Texture == RenderCommandBuffer.LabelTexture)
            {
                continue;
            }

            if (s.X >= camera.Left + camera.Width || s.Y >= camera.Top + camera.Height || s.X + s.Width <= camera.Left || s.Y + s.Height <= camera.Top)
            {
                continue;
            }

            commands.Draw(s.Texture, s.SrcX, s.SrcY, s.Width, s.Height, s.X - camera.Left, s.Y - camera.Top, s.Flags, s.Tint);
        }

        if (!deferLabels)
        {
            _count = 0;
        }
    }

    /// <summary>Records any <see cref="RenderCommandBuffer.LabelTexture"/> entries deferred by <see cref="Flush"/> and empties the batch.</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    public void FlushLabels(RenderCommandBuffer commands, in Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(commands);

        for (int i = 0; i < _count; i++)
        {
            ref readonly Sprite s = ref _sprites[_keys[i].Index];
            if (s.Texture != RenderCommandBuffer.LabelTexture)
            {
                continue;
            }

            if (s.X >= camera.Left + camera.Width || s.Y >= camera.Top + camera.Height || s.X + s.Width <= camera.Left || s.Y + s.Height <= camera.Top)
            {
                continue;
            }

            commands.Draw(s.Texture, s.SrcX, s.SrcY, s.Width, s.Height, s.X - camera.Left, s.Y - camera.Top, s.Flags, s.Tint);
        }

        _count = 0;
    }

    private readonly record struct Sprite(int Texture, int SrcX, int SrcY, int Width, int Height, int X, int Y, int Flags, int Tint);

    private readonly record struct SortKey(int Y, int X, int Id, int Index) : IComparable<SortKey>
    {
        public int CompareTo(SortKey other)
        {
            int c = this.Y.CompareTo(other.Y);
            if (c != 0)
            {
                return c;
            }

            c = this.X.CompareTo(other.X);
            if (c != 0)
            {
                return c;
            }

            c = this.Id.CompareTo(other.Id);
            return c != 0 ? c : this.Index.CompareTo(other.Index);
        }
    }
}
