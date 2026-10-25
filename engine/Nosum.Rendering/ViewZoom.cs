// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>Picks the whole-number zoom a map is drawn at.</summary>
public static class ViewZoom
{
    /// <summary>
    /// Picks the zoom: about <paramref name="tilesAcross"/> tiles across the canvas, except for a room (a map no wider than that) which is drawn
    /// as large as still shows all of it above the strip the interface keeps at the bottom of the canvas.
    /// </summary>
    /// <param name="worldWidth">Map width, pixels.</param>
    /// <param name="worldHeight">Map height, pixels.</param>
    /// <param name="tileWidth">Tile width, pixels.</param>
    /// <param name="canvasWidth">Canvas width, CSS pixels.</param>
    /// <param name="canvasHeight">Canvas height, CSS pixels.</param>
    /// <param name="bottomInset">Height of the canvas strip kept free for the interface, CSS pixels; a room is centred above it.</param>
    /// <param name="tilesAcross">How many tiles an open map shows across.</param>
    /// <param name="maxScale">The largest zoom.</param>
    /// <returns>The zoom, 2 to <paramref name="maxScale"/>.</returns>
    public static int Pick(int worldWidth, int worldHeight, int tileWidth, int canvasWidth, int canvasHeight, int bottomInset, int tilesAcross, int maxScale)
    {
        int open = Math.Clamp((int)Math.Round(canvasWidth / (double)(tilesAcross * tileWidth)), 2, maxScale);
        if (worldWidth > tilesAcross * tileWidth)
        {
            return open;
        }

        int room = (int)Math.Min(canvasWidth / (double)worldWidth, Math.Max(0, canvasHeight - bottomInset) / (double)worldHeight);
        return Math.Clamp(room, 2, maxScale);
    }
}
