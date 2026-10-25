// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Core;

/// <summary>
/// A building or prop drawn upright over a map: a rectangle of a sheet plus a collision
/// footprint (docs/adr/ADR-005 D2, D6). Pure data, usable on any map.
/// </summary>
/// <param name="Name">Stable name.</param>
/// <param name="Sheet">Sheet name the drawer resolves to a texture.</param>
/// <param name="SrcX">Source left in the sheet, pixels.</param>
/// <param name="SrcY">Source top in the sheet, pixels.</param>
/// <param name="Width">Image width, pixels.</param>
/// <param name="Height">Image height, pixels.</param>
/// <param name="FootprintWidth">Footprint width, tiles. The image is centred on it.</param>
/// <param name="FootprintHeight">Footprint height, tiles. The image bottom sits on its bottom edge.</param>
/// <param name="FrontHeight">Pixels of the image top (roof) drawn over everything instead of depth-sorted; 0 for none.</param>
public sealed record PropDefinition(
    string Name,
    string Sheet,
    int SrcX,
    int SrcY,
    int Width,
    int Height,
    int FootprintWidth,
    int FootprintHeight,
    int FrontHeight = 0);

/// <summary>One prop standing on a map.</summary>
/// <param name="Definition">What it is.</param>
/// <param name="TileX">Footprint left tile.</param>
/// <param name="TileY">Footprint top tile.</param>
public readonly record struct PropPlacement(PropDefinition Definition, int TileX, int TileY);
