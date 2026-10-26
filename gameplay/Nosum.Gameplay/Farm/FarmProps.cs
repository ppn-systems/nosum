// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Core;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// The farm's buildings as data: sheet rectangles, footprints and positions in the early
/// <c>Farm.tmx</c> (docs/adr/ADR-007). Names are keys, not display text. Lives in gameplay so the
/// client (prediction) and the server (move validation) build the identical <see cref="WalkGrid"/>.
/// </summary>
public static class FarmProps
{
    /// <summary>Sheet name of <c>sprites/buildings/farmhouse.png</c>.</summary>
    public const string HousesSheet = "buildings/farmhouse";

    /// <summary>Sheet name of <c>sprites/buildings/greenhouse.png</c>.</summary>
    public const string GreenhouseSheet = "buildings/greenhouse";

    /// <summary>Sheet name of <c>sprites/buildings/shipping_bin.png</c>.</summary>
    public const string ShippingBinSheet = "buildings/shipping_bin";

    /// <summary>Tile in front of the farmhouse door, where a new farmer appears.</summary>
    public static readonly (int X, int Y) FarmhouseDoorFront = (64, 16);

    /// <summary>Starter farmhouse: 144 × 144 image, 9 × 6 tile footprint, top 3 tiles are roof.</summary>
    public static readonly PropDefinition Farmhouse = new("farmhouse", HousesSheet, 16, 0, 144, 144, 9, 6, FrontHeight: 48);

    /// <summary>Repaired greenhouse (lower half of its sheet): 112 × 160 image, 7 × 6 footprint, top 4 tiles are roof.</summary>
    public static readonly PropDefinition Greenhouse = new("greenhouse", GreenhouseSheet, 0, 160, 112, 160, 7, 6, FrontHeight: 64);

    /// <summary>Shipping bin: 32 × 32 image, 2 × 1 footprint.</summary>
    public static readonly PropDefinition ShippingBin = new("shipping_bin", ShippingBinSheet, 0, 0, 32, 32, 2, 1);

    /// <summary>Gets the placements on the standard farm layout.</summary>
    public static readonly PropPlacement[] Placements =
    [
        new(Farmhouse, 59, 10),
        new(Greenhouse, 25, 10),
        new(ShippingBin, 71, 14),
    ];

    /// <summary>
    /// Builds the farm's walk grid: the map's own blocking rules plus the footprints of
    /// <see cref="Placements"/>. Client and server must both use this.
    /// </summary>
    /// <param name="map">The parsed <c>Farm.tmx</c>.</param>
    /// <returns>The grid.</returns>
    public static WalkGrid BuildWalkGrid(TileMap map)
    {
        WalkGrid grid = WalkGrid.FromMap(map);
        grid.BlockFootprints(Placements);
        return grid;
    }
}
