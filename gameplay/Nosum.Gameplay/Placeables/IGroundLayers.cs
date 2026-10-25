// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Placeables;

/// <summary>What else may stand on the tiles of a place and so refuses a placement there (docs/adr/ADR-024 D3).</summary>
public interface IGroundLayers
{
    /// <summary>Tells whether a tile is tilled soil.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when the farm layer has worked the tile.</returns>
    bool IsTilled(int x, int y);

    /// <summary>Tells whether a tile holds a resource node (a tree, a stone, a weed, grass).</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when a node stands there.</returns>
    bool HasNode(int x, int y);
}
