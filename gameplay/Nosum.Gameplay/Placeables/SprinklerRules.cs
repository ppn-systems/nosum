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
    private static readonly (int Dx, int Dy)[] s_cardinalOffsets = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>Returns the tile offsets watered by a sprinkler item (<c>ItemCatalog.Sprinkler</c> waters 4 orthogonal tiles).</summary>
    /// <param name="itemId">Placed item id.</param>
    /// <returns>The relative tile offsets watered by that sprinkler, or an empty span when the item is not a sprinkler.</returns>
    public static ReadOnlySpan<(int Dx, int Dy)> GetSprinklerOffsets(int itemId) => itemId switch
    {
        Items.ItemCatalog.Sprinkler => s_cardinalOffsets,
        _ => [],
    };

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
    public static bool IsWatered(IEnumerable<PlacedItem> placed, int tileX, int tileY) => WateredSinceMs(placed, tileX, tileY) is not null;

    /// <summary>Returns when the first sprinkler that waters a tile was placed.</summary>
    /// <param name="placed">The pieces standing in the instance.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns>The earliest placement time, Unix milliseconds, of a sprinkler on one of the tile's four orthogonal neighbours; <see langword="null"/> when none does.</returns>
    public static long? WateredSinceMs(IEnumerable<PlacedItem> placed, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(placed);

        long? since = null;
        foreach (PlacedItem item in placed)
        {
            if (PlaceableCatalog.TryGet(item.ItemId, out PlaceableDefinition? definition)
                && definition.Kind == PlaceableKind.Sprinkler
                && Waters(item.TileX, item.TileY, tileX, tileY))
            {
                since = since is { } earlier ? Math.Min(earlier, item.PlacedAtMs) : item.PlacedAtMs;
            }
        }

        return since;
    }
}
