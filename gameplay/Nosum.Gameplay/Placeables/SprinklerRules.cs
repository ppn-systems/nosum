// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Gameplay.Placeables;

/// <summary>
/// Where a sprinkler waters (docs/adr/ADR-024 D4): the four orthogonal neighbours of its tile, from the start of every game day. A pure function of
/// the placed set, so the crop-growth rule can ask "is this tile watered on day N?" with no tick and no stored state.
/// </summary>
public static class SprinklerRules
{
    /// <summary>Gets the offsets of the tiles a sprinkler waters.</summary>
    public static readonly (int Dx, int Dy)[] Reach = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>Tells whether one sprinkler waters a tile.</summary>
    /// <param name="sprinklerX">The sprinkler's tile x.</param>
    /// <param name="sprinklerY">The sprinkler's tile y.</param>
    /// <param name="tileX">Tile x asked about.</param>
    /// <param name="tileY">Tile y asked about.</param>
    /// <returns><see langword="true"/> for the four orthogonal neighbours; the sprinkler's own tile is not watered.</returns>
    public static bool Waters(int sprinklerX, int sprinklerY, int tileX, int tileY)
        => Math.Abs(sprinklerX - tileX) + Math.Abs(sprinklerY - tileY) == 1;

    /// <summary>Tells whether any sprinkler of a placed set waters a tile.</summary>
    /// <param name="placed">The pieces standing in the instance.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns><see langword="true"/> when a sprinkler stands on one of the tile's four orthogonal neighbours.</returns>
    public static bool IsWatered(IEnumerable<PlacedItem> placed, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(placed);

        foreach (PlacedItem item in placed)
        {
            if (PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition)
                && definition.Kind == PlaceableKind.Sprinkler
                && Waters(item.TileX, item.TileY, tileX, tileY))
            {
                return true;
            }
        }

        return false;
    }
}
