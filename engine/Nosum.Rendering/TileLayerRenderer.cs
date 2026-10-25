// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>
/// Encodes the visible part of a tile layer: only tiles that intersect the camera are drawn.
/// </summary>
public static class TileLayerRenderer
{
    /// <summary>
    /// Records the layer's visible tiles.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="layer">One of the map's layers.</param>
    /// <param name="tileSetTextures">Backend texture id per entry of <see cref="TileMap.TileSets"/>; negative skips the set.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="commands">Where draws are recorded.</param>
    public static void Draw(TileMap map, TileLayer layer, ReadOnlySpan<int> tileSetTextures, in Camera2D camera, RenderCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(commands);

        int tw = map.TileWidth;
        int th = map.TileHeight;
        int x0 = Math.Max(0, Floor(camera.Left, tw));
        int y0 = Math.Max(0, Floor(camera.Top, th));
        int x1 = Math.Min(map.Width - 1, Floor(camera.Left + camera.Width - 1, tw));
        int y1 = Math.Min(map.Height - 1, Floor(camera.Top + camera.Height - 1, th));

        int[] gids = layer.Gids;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int gid = gids[(y * map.Width) + x];
                if (gid == 0)
                {
                    continue;
                }

                int setIndex = map.FindTileSet(gid);
                if (setIndex < 0 || tileSetTextures[setIndex] < 0)
                {
                    continue;
                }

                TileSet set = map.TileSets[setIndex];
                int local = gid - set.FirstGid;
                commands.Draw(
                    tileSetTextures[setIndex],
                    local % set.Columns * tw,
                    local / set.Columns * th,
                    tw,
                    th,
                    (x * tw) - camera.Left,
                    (y * th) - camera.Top);
            }
        }
    }

    private static int Floor(int value, int size) => value >= 0 ? value / size : ((value + 1) / size) - 1;
}
