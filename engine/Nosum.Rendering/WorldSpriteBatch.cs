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

        bool overlay = id is >= 8900 and <= 9999;
        _sprites[_count] = new Sprite(texture, srcX, srcY, width, height, worldX, worldY, flags, tint, overlay);
        _keys[_count] = new SortKey(sortY, sortX, id, _count);
        _count++;
    }

    /// <summary>
    /// Adds a high-priority overlay sprite that is deferred until <see cref="FlushLabels"/> when <c>deferLabels</c> is enabled,
    /// drawing above roofs and ambient darkness without relying on magic sort-key id ranges.
    /// </summary>
    /// <param name="texture">Backend texture id.</param>
    /// <param name="srcX">Source left.</param>
    /// <param name="srcY">Source top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="worldX">Destination left, world pixels.</param>
    /// <param name="worldY">Destination top, world pixels.</param>
    /// <param name="sortY">Anchor y, world pixels.</param>
    /// <param name="sortX">Anchor x, world pixels, breaking ties in y.</param>
    /// <param name="id">Stable id breaking remaining ties.</param>
    /// <param name="flags">Draw flags such as <see cref="RenderCommandBuffer.FlipX"/>.</param>
    /// <param name="tint">Multiply tint or <see cref="RenderCommandBuffer.NoTint"/>.</param>
    public void AddOverlay(int texture, int srcX, int srcY, int width, int height, int worldX, int worldY, int sortY, int sortX = 0, int id = 0, int flags = 0, int tint = RenderCommandBuffer.NoTint)
    {
        if (_count == _sprites.Length)
        {
            return;
        }

        _sprites[_count] = new Sprite(texture, srcX, srcY, width, height, worldX, worldY, flags, tint, Overlay: true);
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

        SortKeys(_keys, _count);
        for (int i = 0; i < _count; i++)
        {
            ref readonly Sprite s = ref _sprites[_keys[i].Index];
            if (deferLabels && (s.Texture == RenderCommandBuffer.LabelTexture || s.Overlay))
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

    /// <summary>Records any <see cref="RenderCommandBuffer.LabelTexture"/> and high-priority overlay entries deferred by <see cref="Flush"/> and empties the batch.</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    public void FlushLabels(RenderCommandBuffer commands, in Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(commands);

        for (int i = 0; i < _count; i++)
        {
            ref readonly Sprite s = ref _sprites[_keys[i].Index];
            if (s.Texture == RenderCommandBuffer.LabelTexture || !s.Overlay)
            {
                continue;
            }

            if (s.X >= camera.Left + camera.Width || s.Y >= camera.Top + camera.Height || s.X + s.Width <= camera.Left || s.Y + s.Height <= camera.Top)
            {
                continue;
            }

            commands.Draw(s.Texture, s.SrcX, s.SrcY, s.Width, s.Height, s.X - camera.Left, s.Y - camera.Top, s.Flags, s.Tint);
        }

        const int padX = 64;
        const int padY = 16;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Sprite s = ref _sprites[_keys[i].Index];
            if (s.Texture != RenderCommandBuffer.LabelTexture)
            {
                continue;
            }

            if (s.X - padX >= camera.Left + camera.Width || s.Y - padY >= camera.Top + camera.Height || s.X + s.Width + padX <= camera.Left || s.Y + s.Height + padY <= camera.Top)
            {
                continue;
            }

            commands.Draw(s.Texture, s.SrcX, s.SrcY, s.Width, s.Height, s.X - camera.Left, s.Y - camera.Top, s.Flags, s.Tint);
        }

        _count = 0;
    }

    private readonly record struct Sprite(int Texture, int SrcX, int SrcY, int Width, int Height, int X, int Y, int Flags, int Tint, bool Overlay = false);

    // An in-place heapsort over the first count keys. Array.Sort on a struct that implements IComparable<T> allocated a few bytes a call on the
    // interpreter build (the frame body, ADR-099), so the sort is written out: no generic comparer, no interface call, nothing allocated. The
    // keys are a total order (Index is unique), so the result is the one Array.Sort gave.
    private static void SortKeys(SortKey[] keys, int count)
    {
        for (int start = (count / 2) - 1; start >= 0; start--)
        {
            SiftDown(keys, start, count);
        }

        for (int end = count - 1; end > 0; end--)
        {
            SortKey top = keys[0];
            keys[0] = keys[end];
            keys[end] = top;
            SiftDown(keys, 0, end);
        }
    }

    private static void SiftDown(SortKey[] keys, int root, int count)
    {
        while (true)
        {
            int child = (2 * root) + 1;
            if (child >= count)
            {
                return;
            }

            if (child + 1 < count && Before(keys[child], keys[child + 1]))
            {
                child++;
            }

            if (!Before(keys[root], keys[child]))
            {
                return;
            }

            SortKey swap = keys[root];
            keys[root] = keys[child];
            keys[child] = swap;
            root = child;
        }
    }

    // The order of SortKey.CompareTo: anchor y, then x, then id, then insertion index.
    private static bool Before(in SortKey a, in SortKey b)
    {
        if (a.Y != b.Y)
        {
            return a.Y < b.Y;
        }

        if (a.X != b.X)
        {
            return a.X < b.X;
        }

        return a.Id != b.Id ? a.Id < b.Id : a.Index < b.Index;
    }

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
