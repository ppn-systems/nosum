// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Placeables;

/// <summary>
/// Which chunks a placed piece stands in (docs/adr/ADR-022). A footprint can cross a chunk border, so a piece belongs to every chunk it overlaps:
/// it is kept in each chunk's bucket, sent to the players who can see any of them and published to each, not only to the chunk of its top-left tile.
/// </summary>
public static class PlacedChunks
{
    /// <summary>Returns the chunks a piece's footprint overlaps, in its rotation.</summary>
    /// <param name="item">The piece.</param>
    /// <returns>The inclusive range from the chunk of its top-left tile to the chunk of its bottom-right tile.</returns>
    public static ChunkRange RangeOf(PlacedItem item)
    {
        int width = 1;
        int height = 1;
        if (PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition))
        {
            PlaceableView view = definition.View(Math.Min(item.Rotation, definition.Rotations - 1));
            width = view.FootprintWidth;
            height = view.FootprintHeight;
        }

        return new ChunkRange(ChunkCoord.OfTile(item.TileX, item.TileY), ChunkCoord.OfTile(item.TileX + width - 1, item.TileY + height - 1));
    }
}
