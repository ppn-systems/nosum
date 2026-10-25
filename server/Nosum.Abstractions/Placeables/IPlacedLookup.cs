// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// A read-only, never-waiting view of what is placed in the instances the server has loaded (docs/adr/ADR-024). The farm layer asks it whether a tile is
/// taken before tilling it, and the crop-growth rule asks whether a sprinkler waters a tile ("watered on day N if a sprinkler covers it": no tick, no state).
/// An instance that was never loaded has nothing placed as far as this view knows; a player entering an instance loads it first.
/// </summary>
public interface IPlacedLookup
{
    /// <summary>Returns the pieces standing in an instance.</summary>
    /// <param name="instance">The place.</param>
    /// <returns>The pieces; empty when none or when the instance is not loaded.</returns>
    IReadOnlyList<PlacedItem> ItemsOf(MapInstance instance);

    /// <summary>Returns every tile that lies under a piece's footprint.</summary>
    /// <param name="instance">The place.</param>
    /// <returns>The tiles; empty when none or when the instance is not loaded.</returns>
    IReadOnlySet<(int X, int Y)> CoveredTiles(MapInstance instance);

    /// <summary>Tells whether a tile lies under a piece's footprint.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns><see langword="true"/> when a piece stands on the tile.</returns>
    bool IsCovered(MapInstance instance, int tileX, int tileY);

    /// <summary>Tells whether a sprinkler waters a tile (<see cref="SprinklerRules"/>).</summary>
    /// <param name="instance">The place.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns><see langword="true"/> when a sprinkler stands on one of the tile's four orthogonal neighbours.</returns>
    bool IsSprinklerWatered(MapInstance instance, int tileX, int tileY);
}
