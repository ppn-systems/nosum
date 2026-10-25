// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Maps;

/// <summary>
/// Where a shop is served on a map: the counter or doorstep tile a player stands next to and presses the action key. The shop itself
/// (what it sells) is <c>Shops.ShopCatalog</c>; this only says where it is (docs/adr/ADR-013 amendments).
/// </summary>
/// <param name="ShopId">The shop, from <c>ShopCatalog</c>; an id from 1000 names a counter that is not a shop (the community board, docs/adr/ADR-030).</param>
/// <param name="TileX">Tile x of the counter.</param>
/// <param name="TileY">Tile y of the counter.</param>
public readonly record struct ShopPoint(int ShopId, int TileX, int TileY);
