// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Maps;

/// <summary>
/// One exit of a map: standing on <c>(FromX, FromY)</c> takes the player to a tile of another map.
/// Tile coordinates, as in the Tiled <c>Warp</c> map property. A source one tile outside the map
/// (for example <c>x = Width</c>) is an edge exit: it is used from the nearest edge tile, walking outwards.
/// </summary>
/// <param name="FromX">Source tile x; <c>-1</c> or the map width mean the left or right edge.</param>
/// <param name="FromY">Source tile y; <c>-1</c> or the map height mean the top or bottom edge.</param>
/// <param name="TargetMap">Stable id of the destination map (a key of <see cref="GameMaps"/>, never a path).</param>
/// <param name="TargetX">Destination tile x.</param>
/// <param name="TargetY">Destination tile y.</param>
public readonly record struct MapWarp(int FromX, int FromY, string TargetMap, int TargetX, int TargetY);
