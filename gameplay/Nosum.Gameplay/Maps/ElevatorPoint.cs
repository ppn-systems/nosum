// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Maps;

/// <summary>
/// Where the mine elevator is served on a map: the tile a player stands next to and presses the action key (docs/adr/ADR-026 D3). The floors it
/// reaches are <c>Mines.MineRules</c>; this only says where it is, like <see cref="ShopPoint"/> for a shop.
/// </summary>
/// <param name="TileX">Tile x of the elevator.</param>
/// <param name="TileY">Tile y of the elevator.</param>
public readonly record struct ElevatorPoint(int TileX, int TileY);
