// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>
/// A camera over a bounded world: the visible rectangle in world pixels, centred on a target
/// and kept inside the world.
/// </summary>
public struct Camera2D
{
    /// <summary>Gets the left edge of the view, world pixels.</summary>
    public int Left { get; private set; }

    /// <summary>Gets the top edge of the view, world pixels.</summary>
    public int Top { get; private set; }

    /// <summary>Gets the view width, world pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the view height, world pixels.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Centres the view on a point, clamped so it does not leave the world; a world smaller
    /// than the view is centred.
    /// </summary>
    /// <param name="centerX">Target x, world pixels.</param>
    /// <param name="centerY">Target y, world pixels.</param>
    /// <param name="viewWidth">View width, world pixels.</param>
    /// <param name="viewHeight">View height, world pixels.</param>
    /// <param name="worldWidth">World width, pixels.</param>
    /// <param name="worldHeight">World height, pixels.</param>
    public void Follow(int centerX, int centerY, int viewWidth, int viewHeight, int worldWidth, int worldHeight)
    {
        this.Width = viewWidth;
        this.Height = viewHeight;
        this.Left = Axis(centerX, viewWidth, worldWidth);
        this.Top = Axis(centerY, viewHeight, worldHeight);
    }

    private static int Axis(int center, int view, int world)
        => world <= view ? (world - view) / 2 : Math.Clamp(center - (view / 2), 0, world - view);
}
