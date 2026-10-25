// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Farm;

namespace Nosum.Gameplay.Items;

/// <summary>
/// The generic item catalogue: every item any feature (farm, market, co-op warehouse, quests) may
/// put in an inventory. Ids are append-only. Ids below 100 are tools, 100-199 seeds, 200-299 produce, 300-399 furniture (placeable, see Nosum.Gameplay.Placeables).
/// </summary>
public static class ItemCatalog
{
    /// <summary>The hoe.</summary>
    public const int Hoe = 1;

    /// <summary>The watering can.</summary>
    public const int WateringCan = 2;

    /// <summary>Parsnip seeds.</summary>
    public const int ParsnipSeeds = 100;

    /// <summary>Radish seeds.</summary>
    public const int RadishSeeds = 101;

    /// <summary>Cauliflower seeds.</summary>
    public const int CauliflowerSeeds = 102;

    /// <summary>A harvested parsnip.</summary>
    public const int Parsnip = 200;

    /// <summary>A harvested radish.</summary>
    public const int Radish = 201;

    /// <summary>A harvested cauliflower.</summary>
    public const int Cauliflower = 202;

    /// <summary>An oak chair.</summary>
    public const int OakChair = 300;

    /// <summary>A blue armchair.</summary>
    public const int BlueArmchair = 301;

    /// <summary>A blue couch.</summary>
    public const int BlueCouch = 302;

    /// <summary>An oak table.</summary>
    public const int OakTable = 303;

    /// <summary>A bookcase.</summary>
    public const int Bookcase = 304;

    /// <summary>A floor lamp.</summary>
    public const int FloorLamp = 305;

    /// <summary>A house plant.</summary>
    public const int HousePlant = 306;

    /// <summary>Largest stack of furniture.</summary>
    public const int FurnitureStack = 20;

    /// <summary>Largest stack of seeds and produce.</summary>
    public const int DefaultStack = 99;

    private static readonly ItemDefinition[] s_all =
    [
        new(Hoe, "hoe", ItemKind.Tool, 1, ToolKind.Hoe),
        new(WateringCan, "watering_can", ItemKind.Tool, 1, ToolKind.WateringCan),
        new(ParsnipSeeds, "parsnip_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Parsnip),
        new(RadishSeeds, "radish_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Radish),
        new(CauliflowerSeeds, "cauliflower_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Cauliflower),
        new(Parsnip, "parsnip", ItemKind.Produce, DefaultStack),
        new(Radish, "radish", ItemKind.Produce, DefaultStack),
        new(Cauliflower, "cauliflower", ItemKind.Produce, DefaultStack),
        new(OakChair, "oak_chair", ItemKind.Furniture, FurnitureStack),
        new(BlueArmchair, "blue_armchair", ItemKind.Furniture, FurnitureStack),
        new(BlueCouch, "blue_couch", ItemKind.Furniture, FurnitureStack),
        new(OakTable, "oak_table", ItemKind.Furniture, FurnitureStack),
        new(Bookcase, "bookcase", ItemKind.Furniture, FurnitureStack),
        new(FloorLamp, "floor_lamp", ItemKind.Furniture, FurnitureStack),
        new(HousePlant, "house_plant", ItemKind.Furniture, FurnitureStack),
    ];

    private static readonly Dictionary<int, ItemDefinition> s_byId = Index();

    /// <summary>Gets every item, in id order.</summary>
    public static IReadOnlyList<ItemDefinition> All => s_all;

    /// <summary>Looks up an item.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="item">The definition when found.</param>
    /// <returns><see langword="true"/> when the id names an item.</returns>
    public static bool TryGet(int itemId, [NotNullWhen(true)] out ItemDefinition? item) => s_byId.TryGetValue(itemId, out item);

    private static Dictionary<int, ItemDefinition> Index()
    {
        Dictionary<int, ItemDefinition> index = new(s_all.Length);
        foreach (ItemDefinition item in s_all)
        {
            index.Add(item.Id, item);
        }

        return index;
    }
}
