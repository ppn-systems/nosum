// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Placeables;

/// <summary>
/// The placeables of the game, keyed by the item they are made from. The art is the placeholder furniture sheet
/// (docs/adr/ADR-007); rectangles were measured on <c>TileSheets/furniture.png</c>, so replacing the art means replacing this table.
/// </summary>
public static class PlaceableCatalog
{
    /// <summary>Sheet name of <c>TileSheets/furniture.png</c>.</summary>
    public const string FurnitureSheet = "furniture";

    /// <summary>Radius of the light a floor lamp gives, pixels (three and a half tiles).</summary>
    public const int FloorLampLightRadius = 56;

    /// <summary>Sheet name of <c>TileSheets/Craftables.png</c>: the crafted machines and containers (placeholder art, docs/adr/ADR-007).</summary>
    public const string CraftablesSheet = "Craftables";

    /// <summary>Sheet name of <c>Maps/springobjects.png</c>: the small objects (fence, path, sprinkler).</summary>
    public const string ObjectsSheet = "springobjects";

    private static readonly PlaceableDefinition[] s_all =
    [..Furniture(), ..Buildings()];

    private static readonly Dictionary<int, PlaceableDefinition> s_byItem = Index();

    private static PlaceableDefinition[] Furniture() =>
    [
        // Chair: one tile; down, right and up are drawn, left mirrors right.
        new(ItemCatalog.OakChair, FurnitureSheet, new PlaceableView(0, 0, 16, 32, 1, 1), new PlaceableView(16, 0, 16, 32, 1, 1), new PlaceableView(32, 0, 16, 32, 1, 1), new PlaceableView(16, 0, 16, 32, 1, 1, FlipX: true)),

        // Armchair: two tiles wide in every rotation.
        new(ItemCatalog.BlueArmchair, FurnitureSheet, new PlaceableView(0, 144, 32, 32, 2, 1), new PlaceableView(32, 140, 32, 36, 2, 1), new PlaceableView(64, 144, 32, 32, 2, 1), new PlaceableView(32, 140, 32, 36, 2, 1, FlipX: true)),

        // Couch: three tiles wide facing down or up, two by two seen from the side.
        new(ItemCatalog.BlueCouch, FurnitureSheet, new PlaceableView(0, 208, 48, 32, 3, 1), new PlaceableView(48, 208, 32, 48, 2, 2), new PlaceableView(80, 208, 48, 32, 3, 1), new PlaceableView(48, 208, 32, 48, 2, 2, FlipX: true)),

        // Table, bookcase, lamp and plant do not rotate; the lamp lights the night.
        new(ItemCatalog.OakTable, FurnitureSheet, new PlaceableView(0, 569, 32, 38, 2, 2)),
        new(ItemCatalog.Bookcase, FurnitureSheet, new PlaceableView(48, 640, 32, 48, 2, 1)),
        new(ItemCatalog.FloorLamp, FurnitureSheet, FloorLampLightRadius, new PlaceableView(48, 733, 16, 34, 1, 1)),
        new(ItemCatalog.HousePlant, FurnitureSheet, new PlaceableView(0, 688, 16, 32, 1, 1)),

        // Crafted objects (docs/adr/ADR-024). Craftables.png cells are 16 x 32, the footprint is the bottom tile.
        new(ItemCatalog.Chest, CraftablesSheet, new PlaceableView(32, 512, 16, 32, 1, 1)) { Kind = PlaceableKind.Chest },
        new(ItemCatalog.Scarecrow, CraftablesSheet, new PlaceableView(0, 32, 16, 32, 1, 1)),
        new(ItemCatalog.Furnace, CraftablesSheet, new PlaceableView(80, 32, 16, 32, 1, 1)) { Kind = PlaceableKind.Furnace },
        new(ItemCatalog.WoodFence, ObjectsSheet, new PlaceableView(160, 208, 16, 16, 1, 1)) { Kind = PlaceableKind.Fence },
        new(ItemCatalog.StonePath, ObjectsSheet, new PlaceableView(368, 256, 16, 16, 1, 1)) { Kind = PlaceableKind.Path, BlocksWalking = false },
        new(ItemCatalog.Sprinkler, ObjectsSheet, new PlaceableView(368, 384, 16, 16, 1, 1)) { Kind = PlaceableKind.Sprinkler },
    ];

    // Farm buildings (docs/adr/ADR-028 D1): the blueprint item is placed like any large piece; sprite, footprint and door come from the building catalogue.
    private static PlaceableDefinition[] Buildings()
    {
        List<PlaceableDefinition> buildings = [];
        foreach (BuildingDefinition building in BuildingCatalog.All)
        {
            buildings.Add(new PlaceableDefinition(building.BlueprintItemId, building.Sheet, building.View) { Kind = PlaceableKind.Building, Door = (building.DoorX, building.DoorY) });
        }

        return [.. buildings];
    }

    /// <summary>Gets every placeable.</summary>
    public static IReadOnlyList<PlaceableDefinition> All => s_all;

    /// <summary>Looks up the placeable an item is made into.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="definition">The definition when the item can be placed.</param>
    /// <returns><see langword="true"/> when the item is a placeable.</returns>
    public static bool TryGet(int itemId, [NotNullWhen(true)] out PlaceableDefinition? definition) => s_byItem.TryGetValue(itemId, out definition);

    private static Dictionary<int, PlaceableDefinition> Index()
    {
        Dictionary<int, PlaceableDefinition> index = new(s_all.Length);
        foreach (PlaceableDefinition definition in s_all)
        {
            index.Add(definition.ItemId, definition);
        }

        return index;
    }
}
