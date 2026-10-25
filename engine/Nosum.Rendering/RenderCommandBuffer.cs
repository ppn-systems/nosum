// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Nosum.Rendering;

/// <summary>
/// One frame's draw list, encoded into a preallocated <see cref="int"/> array so it crosses to
/// the backend as one byte span (docs/adr/ADR-002 D2). Nothing is allocated per frame.
/// </summary>
/// <remarks>
/// Layout (little-endian <c>int32</c>): header <c>[count, scale]</c>, then <c>count</c> draws of
/// <see cref="IntsPerCommand"/> values: <c>texture, srcX, srcY, srcW, srcH, dstX, dstY, flags,
/// tint</c>. Destination is in screen pixels before scaling; <c>tint</c> is <c>0xRRGGBBAA</c>,
/// <see cref="NoTint"/> meaning none. A draw with texture <see cref="BlackFill"/> fills a rectangle with black at the opacity in <c>tint</c>
/// (<see cref="FillBlack"/>). A draw with texture <see cref="LabelTexture"/> draws a pre-rendered text label (<see cref="DrawLabel"/>): <c>srcX</c> is the label id
/// the backend was given by <see cref="LabelRegistry"/>, the destination is the label's bottom centre. <c>renderer.js</c> decodes the same layout.
/// </remarks>
public sealed class RenderCommandBuffer
{
    /// <summary>Values per draw command.</summary>
    public const int IntsPerCommand = 9;

    /// <summary>Header values before the first command.</summary>
    public const int HeaderInts = 2;

    /// <summary>Tint value meaning "draw unchanged".</summary>
    public const int NoTint = -1;

    /// <summary>Texture id of a command that fills a rectangle with black instead of drawing a sprite; <c>tint</c> then holds the opacity.</summary>
    public const int BlackFill = -2;

    /// <summary>Texture id of a command that draws a text label instead of a sprite; <c>srcX</c> then holds the label id (see <see cref="LabelRegistry"/>).</summary>
    public const int LabelTexture = -3;

    /// <summary>Flag: mirror the sprite horizontally.</summary>
    public const int FlipX = 1;

    private readonly int[] _data;

    /// <summary>Initializes a buffer.</summary>
    /// <param name="capacity">Maximum draws per frame.</param>
    public RenderCommandBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.Capacity = capacity;
        _data = new int[HeaderInts + (capacity * IntsPerCommand)];
    }

    /// <summary>Gets the maximum number of draws per frame.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of draws recorded since <see cref="Begin"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Starts a new frame.</summary>
    /// <param name="scale">Whole-number screen scale the backend applies to every draw.</param>
    public void Begin(int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        this.Count = 0;
        _data[1] = scale;
    }

    /// <summary>
    /// Records one draw. Draws past <see cref="Capacity"/> are dropped.
    /// </summary>
    /// <param name="texture">Backend texture id.</param>
    /// <param name="srcX">Source left, pixels.</param>
    /// <param name="srcY">Source top, pixels.</param>
    /// <param name="width">Width, pixels.</param>
    /// <param name="height">Height, pixels.</param>
    /// <param name="dstX">Destination left, unscaled screen pixels.</param>
    /// <param name="dstY">Destination top, unscaled screen pixels.</param>
    /// <param name="flags">Draw flags such as <see cref="FlipX"/>.</param>
    /// <param name="tint">Multiply tint as <c>0xRRGGBBAA</c>, or <see cref="NoTint"/>.</param>
    public void Draw(int texture, int srcX, int srcY, int width, int height, int dstX, int dstY, int flags = 0, int tint = NoTint)
    {
        if (this.Count == this.Capacity)
        {
            return;
        }

        int i = HeaderInts + (this.Count * IntsPerCommand);
        _data[i] = texture;
        _data[i + 1] = srcX;
        _data[i + 2] = srcY;
        _data[i + 3] = width;
        _data[i + 4] = height;
        _data[i + 5] = dstX;
        _data[i + 6] = dstY;
        _data[i + 7] = flags;
        _data[i + 8] = tint;
        this.Count++;
    }

    /// <summary>
    /// Records a black rectangle over everything drawn so far, for fades between scenes. Nothing is recorded for zero opacity.
    /// </summary>
    /// <param name="opacity">0 (clear) to 255 (solid black).</param>
    /// <param name="width">Rectangle width, unscaled screen pixels.</param>
    /// <param name="height">Rectangle height, unscaled screen pixels.</param>
    public void FillBlack(int opacity, int width, int height)
    {
        if (opacity > 0)
        {
            this.Draw(BlackFill, 0, 0, width, height, 0, 0, 0, Math.Min(opacity, 255));
        }
    }

    /// <summary>
    /// Records a text label (a nickname above a head), centred on <paramref name="centerX"/> with its bottom edge at <paramref name="bottomY"/>.
    /// The text itself never crosses per frame: it was uploaded once with <see cref="LabelRegistry"/> and only its id is recorded here.
    /// </summary>
    /// <param name="labelId">Label id from <see cref="LabelRegistry"/>.</param>
    /// <param name="centerX">Centre x, unscaled screen pixels.</param>
    /// <param name="bottomY">Bottom y, unscaled screen pixels.</param>
    public void DrawLabel(int labelId, int centerX, int bottomY)
    {
        if (labelId > 0)
        {
            this.Draw(LabelTexture, labelId, 0, 0, 0, centerX, bottomY);
        }
    }

    /// <summary>
    /// Returns the encoded frame (header plus recorded draws) as bytes over the internal array.
    /// </summary>
    /// <returns>The frame bytes; valid until the next <see cref="Begin"/>.</returns>
    public Span<byte> Encode()
    {
        _data[0] = this.Count;
        return MemoryMarshal.AsBytes(_data.AsSpan(0, HeaderInts + (this.Count * IntsPerCommand)));
    }
}
