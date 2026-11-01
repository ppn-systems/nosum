// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Placeables;

/// <summary>
/// The placeables of the game, keyed by the item they are made from. The furniture art is <c>sprites/furniture.png</c>: the early pieces
/// (docs/adr/ADR-007) and the decorating pieces cut from the Kenney packs (docs/adr/ADR-073), at the rectangles named here.
/// </summary>
public static class PlaceableCatalog
{
    /// <summary>Sheet name of <c>sprites/furniture.png</c>.</summary>
    public const string FurnitureSheet = "furniture";

    /// <summary>Radius of the light a floor lamp gives, pixels (three and a half tiles).</summary>
    public const int FloorLampLightRadius = 56;

    /// <summary>Radius of the light a candelabra or a paper lantern gives, pixels (three tiles).</summary>
    public const int CandleLightRadius = 48;

    /// <summary>Radius of the light a table lamp or a wall sconce gives, pixels (two and a half tiles).</summary>
    public const int SmallLightRadius = 40;

    /// <summary>Sheet name of <c>sprites/craftables.png</c>: the crafted machines and containers (early art, docs/adr/ADR-007).</summary>
    public const string CraftedSheet = "craftables";

    /// <summary>Sheet name of <c>sprites/items.png</c>: the small objects (fence, path, sprinkler).</summary>
    public const string ObjectsSheet = "items";

    private static readonly PlaceableDefinition[] s_all =
    [..Furniture(), ..Buildings()];

    private static readonly Dictionary<int, PlaceableDefinition> s_byItem = Index();

    private static PlaceableDefinition[] Furniture() =>
    [
        // Chair: one tile; down, right and up are drawn, left mirrors right.
        new(ItemCatalog.OakChair, FurnitureSheet, new PlaceableView(0, 0, 16, 32, 1, 1), new PlaceableView(16, 0, 16, 32, 1, 1), new PlaceableView(32, 0, 16, 32, 1, 1), new PlaceableView(16, 0, 16, 32, 1, 1, FlipX: true)) { Category = FurnitureCategory.Seating },

        // Armchair: two tiles wide in every rotation.
        new(ItemCatalog.BlueArmchair, FurnitureSheet, new PlaceableView(0, 144, 32, 32, 2, 1), new PlaceableView(32, 140, 32, 36, 2, 1), new PlaceableView(64, 144, 32, 32, 2, 1), new PlaceableView(32, 140, 32, 36, 2, 1, FlipX: true)) { Category = FurnitureCategory.Seating },

        // Couch: three tiles wide facing down or up, two by two seen from the side.
        new(ItemCatalog.BlueCouch, FurnitureSheet, new PlaceableView(0, 208, 48, 32, 3, 1), new PlaceableView(48, 208, 32, 48, 2, 2), new PlaceableView(80, 208, 48, 32, 3, 1), new PlaceableView(48, 208, 32, 48, 2, 2, FlipX: true)) { Category = FurnitureCategory.Seating },

        // Table, bookcase, lamp and plant do not rotate; the lamp lights the night.
        new(ItemCatalog.OakTable, FurnitureSheet, new PlaceableView(0, 569, 32, 38, 2, 2)) { Category = FurnitureCategory.Tables },
        new(ItemCatalog.Bookcase, FurnitureSheet, new PlaceableView(48, 640, 32, 48, 2, 1)) { Category = FurnitureCategory.Storage },
        new(ItemCatalog.FloorLamp, FurnitureSheet, FloorLampLightRadius, new PlaceableView(48, 733, 16, 34, 1, 1)) { Category = FurnitureCategory.Lighting },
        new(ItemCatalog.HousePlant, FurnitureSheet, new PlaceableView(0, 688, 16, 32, 1, 1)) { Category = FurnitureCategory.Plants },

        // Decorating pieces (docs/adr/ADR-073). Standing pieces block walking; rugs, paintings and wall pieces do not.
        // Chairs and benches: down, right and up are drawn, left mirrors right.
        new(ItemCatalog.WoodenStool, FurnitureSheet, new PlaceableView(32, 1632, 16, 16, 1, 1)) { Category = FurnitureCategory.Seating },
        new(ItemCatalog.CushionedChair, FurnitureSheet, new PlaceableView(224, 1552, 16, 32, 1, 1), new PlaceableView(256, 1552, 16, 32, 1, 1), new PlaceableView(288, 1552, 16, 32, 1, 1), new PlaceableView(256, 1552, 16, 32, 1, 1, FlipX: true)) { Category = FurnitureCategory.Seating },
        new(ItemCatalog.WoodenBench, FurnitureSheet, new PlaceableView(48, 1600, 32, 16, 2, 1), new PlaceableView(416, 1552, 16, 32, 1, 2), new PlaceableView(96, 1600, 32, 16, 2, 1), new PlaceableView(416, 1552, 16, 32, 1, 2, FlipX: true)) { Category = FurnitureCategory.Seating },
        new(ItemCatalog.GreenArmchair, FurnitureSheet, new PlaceableView(336, 1488, 32, 32, 2, 1), new PlaceableView(384, 1488, 32, 36, 2, 1), new PlaceableView(432, 1488, 32, 32, 2, 1), new PlaceableView(384, 1488, 32, 36, 2, 1, FlipX: true)) { Category = FurnitureCategory.Seating },

        new(ItemCatalog.RoundTable, FurnitureSheet, new PlaceableView(96, 1552, 32, 32, 2, 2)) { Category = FurnitureCategory.Tables },
        new(ItemCatalog.SideTable, FurnitureSheet, new PlaceableView(368, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Tables },
        new(ItemCatalog.LongTable, FurnitureSheet, new PlaceableView(224, 1488, 48, 32, 3, 1)) { Category = FurnitureCategory.Tables },

        new(ItemCatalog.SingleBed, FurnitureSheet, new PlaceableView(64, 1488, 32, 48, 2, 2)) { Category = FurnitureCategory.Beds },
        new(ItemCatalog.DoubleBed, FurnitureSheet, new PlaceableView(0, 1488, 48, 48, 3, 2)) { Category = FurnitureCategory.Beds },

        new(ItemCatalog.Wardrobe, FurnitureSheet, new PlaceableView(112, 1488, 32, 48, 2, 1)) { Category = FurnitureCategory.Storage },
        new(ItemCatalog.ChestOfDrawers, FurnitureSheet, new PlaceableView(288, 1488, 32, 32, 2, 1)) { Category = FurnitureCategory.Storage },
        new(ItemCatalog.KitchenCabinet, FurnitureSheet, new PlaceableView(320, 1552, 16, 32, 1, 1)) { Category = FurnitureCategory.Storage },
        new(ItemCatalog.WallShelf, FurnitureSheet, new PlaceableView(0, 1600, 32, 16, 2, 1)) { Category = FurnitureCategory.Storage, BlocksWalking = false },

        new(ItemCatalog.Candelabra, FurnitureSheet, CandleLightRadius, new PlaceableView(192, 1552, 16, 32, 1, 1)) { Category = FurnitureCategory.Lighting },
        new(ItemCatalog.TableLamp, FurnitureSheet, SmallLightRadius, new PlaceableView(400, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Lighting },
        new(ItemCatalog.WallSconce, FurnitureSheet, SmallLightRadius, new PlaceableView(464, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Lighting, BlocksWalking = false },
        new(ItemCatalog.PaperLantern, FurnitureSheet, CandleLightRadius, new PlaceableView(272, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Lighting, SoldIn = Season.Fall },

        new(ItemCatalog.PottedFern, FurnitureSheet, new PlaceableView(384, 1552, 16, 32, 1, 1)) { Category = FurnitureCategory.Plants },
        new(ItemCatalog.CactusPot, FurnitureSheet, new PlaceableView(176, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Plants },
        new(ItemCatalog.FlowerBox, FurnitureSheet, new PlaceableView(240, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Plants, SoldIn = Season.Spring },
        new(ItemCatalog.Bonsai, FurnitureSheet, new PlaceableView(144, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Plants },

        new(ItemCatalog.LandscapePainting, FurnitureSheet, new PlaceableView(448, 1552, 32, 16, 2, 1)) { Category = FurnitureCategory.Decor, BlocksWalking = false },
        new(ItemCatalog.PortraitPainting, FurnitureSheet, new PlaceableView(304, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Decor, BlocksWalking = false },
        new(ItemCatalog.Window, FurnitureSheet, new PlaceableView(144, 1552, 32, 32, 2, 1)) { Category = FurnitureCategory.Decor, BlocksWalking = false },
        new(ItemCatalog.WallClock, FurnitureSheet, new PlaceableView(432, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Decor, BlocksWalking = false },
        new(ItemCatalog.Mirror, FurnitureSheet, new PlaceableView(352, 1552, 16, 32, 1, 1)) { Category = FurnitureCategory.Decor, BlocksWalking = false },
        new(ItemCatalog.PumpkinDecor, FurnitureSheet, new PlaceableView(336, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Decor, SoldIn = Season.Fall },
        new(ItemCatalog.WinterWreath, FurnitureSheet, new PlaceableView(0, 1632, 16, 16, 1, 1)) { Category = FurnitureCategory.Decor, BlocksWalking = false, SoldIn = Season.Winter },

        new(ItemCatalog.RedRug, FurnitureSheet, new PlaceableView(0, 1552, 32, 32, 2, 2)) { Category = FurnitureCategory.Rugs, BlocksWalking = false },
        new(ItemCatalog.BlueRug, FurnitureSheet, new PlaceableView(160, 1488, 48, 32, 3, 2)) { Category = FurnitureCategory.Rugs, BlocksWalking = false },
        new(ItemCatalog.RoundRug, FurnitureSheet, new PlaceableView(48, 1552, 32, 32, 2, 2)) { Category = FurnitureCategory.Rugs, BlocksWalking = false },
        new(ItemCatalog.Doormat, FurnitureSheet, new PlaceableView(208, 1600, 16, 16, 1, 1)) { Category = FurnitureCategory.Rugs, BlocksWalking = false },

        // Crafted objects (docs/adr/ADR-024). craftables.png cells are 16 x 32, the footprint is the bottom tile.
        new(ItemCatalog.Chest, CraftedSheet, new PlaceableView(32, 512, 16, 32, 1, 1)) { Kind = PlaceableKind.Chest, Category = FurnitureCategory.Storage },
        new(ItemCatalog.Scarecrow, CraftedSheet, new PlaceableView(0, 32, 16, 32, 1, 1)),
        new(ItemCatalog.Furnace, CraftedSheet, new PlaceableView(80, 32, 16, 32, 1, 1)) { Kind = PlaceableKind.Furnace, Category = FurnitureCategory.Equipment },
        new(ItemCatalog.WoodFence, CraftedSheet, new PlaceableView(0, 64, 16, 16, 1, 1)) { Kind = PlaceableKind.Fence },
        new(ItemCatalog.StonePath, ObjectsSheet, new PlaceableView(112, 32, 16, 16, 1, 1)) { Kind = PlaceableKind.Path, BlocksWalking = false },
        new(ItemCatalog.Sprinkler, ObjectsSheet, new PlaceableView(128, 32, 16, 16, 1, 1)) { Kind = PlaceableKind.Sprinkler, Category = FurnitureCategory.Equipment },

        // The guestbook shares the scarecrow's cell until it has art of its own (docs/adr/ADR-007).
        new(ItemCatalog.Guestbook, CraftedSheet, new PlaceableView(0, 32, 16, 32, 1, 1)) { Kind = PlaceableKind.Guestbook },
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
