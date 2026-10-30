// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Rendering;

/// <summary>Tests whether a world rectangle is on screen, so a layer skips what the camera does not see (docs/adr/ADR-071 D4).</summary>
public static class ViewCulling
{
    /// <summary>Whether a rectangle in world pixels overlaps the camera's view. Edges that only touch do not count.</summary>
    /// <param name="left">The left edge, world pixels.</param>
    /// <param name="top">The top edge, world pixels.</param>
    /// <param name="width">The width, pixels.</param>
    /// <param name="height">The height, pixels.</param>
    /// <param name="camera">The camera.</param>
    public static bool Overlaps(int left, int top, int width, int height, in Camera2D camera) =>
        left < camera.Left + camera.Width
        && left + width > camera.Left
        && top < camera.Top + camera.Height
        && top + height > camera.Top;
}
