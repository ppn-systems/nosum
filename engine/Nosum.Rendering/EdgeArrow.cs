// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>Which way an <see cref="EdgeArrow"/> points.</summary>
public enum EdgeArrowDirection : byte
{
    /// <summary>Toward the top of the view.</summary>
    Up = 0,

    /// <summary>Toward the right of the view.</summary>
    Right = 1,

    /// <summary>Toward the bottom of the view.</summary>
    Down = 2,

    /// <summary>Toward the left of the view.</summary>
    Left = 3,
}

/// <summary>Where an <see cref="EdgeArrow"/> is drawn: the top left corner of its box, and the way it points.</summary>
/// <param name="X">Left edge of the arrow's box, view pixels.</param>
/// <param name="Y">Top edge of the arrow's box, view pixels.</param>
/// <param name="Direction">The way the arrow points.</param>
public readonly record struct EdgeArrowPlacement(int X, int Y, EdgeArrowDirection Direction);

/// <summary>
/// A waypoint arrow at the edge of the view that points to something off screen (docs/adr/ADR-093 D2). <see cref="TryPlace"/> is pure: it puts the arrow where the
/// ray from the view's centre to the target leaves the view, inset from the edges. <see cref="Draw"/> records the arrow with rectangles only, so it costs no
/// allocation and no interop beyond the frame's one submit.
/// </summary>
public static class EdgeArrow
{
    /// <summary>Side of the arrow's box, view pixels. The shape is drawn in a box this size.</summary>
    public const int Size = 9;

    /// <summary>Places the arrow for a target. Nothing is placed when the target is already inside the inset view.</summary>
    /// <param name="targetX">Target x, view pixels (may lie off the view).</param>
    /// <param name="targetY">Target y, view pixels (may lie off the view).</param>
    /// <param name="viewWidth">View width, view pixels.</param>
    /// <param name="viewHeight">View height, view pixels.</param>
    /// <param name="inset">Distance the arrow keeps from each edge, view pixels.</param>
    /// <param name="placement">The arrow when one is needed.</param>
    /// <returns><see langword="true"/> when the target is off the inset view and <paramref name="placement"/> is set.</returns>
    public static bool TryPlace(double targetX, double targetY, int viewWidth, int viewHeight, int inset, out EdgeArrowPlacement placement)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(viewWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(inset);

        placement = default;
        double halfW = (viewWidth / 2.0) - inset;
        double halfH = (viewHeight / 2.0) - inset;
        if (halfW <= 0 || halfH <= 0)
        {
            return false;
        }

        double cx = viewWidth / 2.0;
        double cy = viewHeight / 2.0;
        double dx = targetX - cx;
        double dy = targetY - cy;
        if (Math.Abs(dx) <= halfW && Math.Abs(dy) <= halfH)
        {
            return false;
        }

        // The ray from the centre to the target leaves the inset rectangle on the side it reaches first.
        double tx = Math.Abs(dx) < double.Epsilon ? double.PositiveInfinity : halfW / Math.Abs(dx);
        double ty = Math.Abs(dy) < double.Epsilon ? double.PositiveInfinity : halfH / Math.Abs(dy);
        bool horizontalEdge = tx <= ty;
        double t = Math.Min(tx, ty);
        double ex = cx + (dx * t);
        double ey = cy + (dy * t);

        EdgeArrowDirection direction = horizontalEdge
            ? (dx < 0 ? EdgeArrowDirection.Left : EdgeArrowDirection.Right)
            : (dy < 0 ? EdgeArrowDirection.Up : EdgeArrowDirection.Down);

        int x = (int)Math.Round(ex) - (Size / 2);
        int y = (int)Math.Round(ey) - (Size / 2);
        placement = new EdgeArrowPlacement(x, y, direction);
        return true;
    }

    /// <summary>Records the arrow: a shadow one pixel down and right, then the arrow in <paramref name="colour"/>.</summary>
    /// <param name="commands">The frame's command buffer.</param>
    /// <param name="placement">Where the arrow is and which way it points.</param>
    /// <param name="colour">Colour as <c>0xRRGGBBAA</c>.</param>
    /// <param name="shadow">Shadow colour as <c>0xRRGGBBAA</c>.</param>
    /// <remarks>
    /// The renderer places the arrow off the HUD (docs/adr/ADR-096 D2): it slides the arrow along its edge until no HUD region covers it, and draws it
    /// there. The shadow command comes first, since the renderer places the arrow from it. The arrow is not drawn when no place on its edge is clear.
    /// </remarks>
    public static void Draw(RenderCommandBuffer commands, in EdgeArrowPlacement placement, int colour, int shadow)
    {
        ArgumentNullException.ThrowIfNull(commands);
        int direction = (int)placement.Direction;
        commands.Draw(RenderCommandBuffer.EdgeArrowTexture, direction, 1, Size, Size, placement.X, placement.Y, 0, shadow);
        commands.Draw(RenderCommandBuffer.EdgeArrowTexture, direction, 0, Size, Size, placement.X, placement.Y, 0, colour);
    }
}
