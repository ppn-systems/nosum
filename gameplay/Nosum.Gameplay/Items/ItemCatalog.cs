// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Items;

/// <summary>
/// The generic item catalogue: every item any feature (farm, market, co-op warehouse, quests) may
/// put in an inventory. Ids are append-only. Ids below 100 are tools, 100-189 crop seeds, 190-199 tree seeds, 200-299 produce, 300-399 furniture (placeable, see Nosum.Gameplay.Placeables), 400-499 clothing, 500-599 resources.
/// </summary>
public static class ItemCatalog
{
    /// <summary>The hoe.</summary>
    public const int Hoe = 1;

    /// <summary>The watering can.</summary>
    public const int WateringCan = 2;

    /// <summary>The axe: trees, stumps and twigs.</summary>
    public const int Axe = 3;

    /// <summary>The pickaxe: stones and ore nodes; clears empty tilled soil.</summary>
    public const int Pickaxe = 4;

    /// <summary>The scythe: weeds and grass; harvests a mature crop.</summary>
    public const int Scythe = 5;

    /// <summary>Parsnip seeds.</summary>
    public const int ParsnipSeeds = 100;

    /// <summary>Radish seeds.</summary>
    public const int RadishSeeds = 101;

    /// <summary>Cauliflower seeds.</summary>
    public const int CauliflowerSeeds = 102;

    /// <summary>An oak seed, from chopping an oak; planted, it grows into an oak.</summary>
    public const int OakSeed = 190;

    /// <summary>A maple seed.</summary>
    public const int MapleSeed = 191;

    /// <summary>A pine seed.</summary>
    public const int PineSeed = 192;

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

    /// <summary>Wood, from trees, stumps and twigs.</summary>
    public const int Wood = 500;

    /// <summary>Stone, from stones.</summary>
    public const int Stone = 501;

    /// <summary>Fiber, from weeds and grass.</summary>
    public const int Fiber = 502;

    /// <summary>Coal, a rare find in stones.</summary>
    public const int Coal = 503;

    /// <summary>Copper ore, from copper ore nodes.</summary>
    public const int CopperOre = 504;

    /// <summary>Largest stack of furniture.</summary>
    public const int FurnitureStack = 20;

    /// <summary>A red shirt.</summary>
    public const int ShirtRed = 400;

    /// <summary>A green shirt.</summary>
    public const int ShirtGreen = 401;

    /// <summary>A blue shirt.</summary>
    public const int ShirtBlue = 402;

    /// <summary>Blue trousers.</summary>
    public const int PantsBlue = 420;

    /// <summary>Khaki trousers.</summary>
    public const int PantsKhaki = 421;

    /// <summary>A straw hat.</summary>
    public const int HatStraw = 440;

    /// <summary>A cap.</summary>
    public const int HatCap = 441;

    /// <summary>Largest stack of seeds and produce.</summary>
    public const int DefaultStack = 99;

    private static readonly ItemDefinition[] s_all =
    [
        new(Hoe, "hoe", ItemKind.Tool, 1, ToolKind.Hoe),
        new(WateringCan, "watering_can", ItemKind.Tool, 1, ToolKind.WateringCan),
        new(Axe, "axe", ItemKind.Tool, 1, ToolKind.Axe),
        new(Pickaxe, "pickaxe", ItemKind.Tool, 1, ToolKind.Pickaxe),
        new(Scythe, "scythe", ItemKind.Tool, 1, ToolKind.Scythe),
        new(ParsnipSeeds, "parsnip_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Parsnip),
        new(RadishSeeds, "radish_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Radish),
        new(CauliflowerSeeds, "cauliflower_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Cauliflower),
        new(OakSeed, "oak_seed", ItemKind.TreeSeed, DefaultStack, treeVariant: TreeVariant.Oak),
        new(MapleSeed, "maple_seed", ItemKind.TreeSeed, DefaultStack, treeVariant: TreeVariant.Maple),
        new(PineSeed, "pine_seed", ItemKind.TreeSeed, DefaultStack, treeVariant: TreeVariant.Pine),
        new(Parsnip, "parsnip", ItemKind.Produce, DefaultStack),
        new(Radish, "radish", ItemKind.Produce, DefaultStack),
        new(Cauliflower, "cauliflower", ItemKind.Produce, DefaultStack),
        new(Wood, "wood", ItemKind.Resource, DefaultStack),
        new(Stone, "stone", ItemKind.Resource, DefaultStack),
        new(Fiber, "fiber", ItemKind.Resource, DefaultStack),
        new(Coal, "coal", ItemKind.Resource, DefaultStack),
        new(CopperOre, "copper_ore", ItemKind.Resource, DefaultStack),
        new(OakChair, "oak_chair", ItemKind.Furniture, FurnitureStack),
        new(BlueArmchair, "blue_armchair", ItemKind.Furniture, FurnitureStack),
        new(BlueCouch, "blue_couch", ItemKind.Furniture, FurnitureStack),
        new(OakTable, "oak_table", ItemKind.Furniture, FurnitureStack),
        new(Bookcase, "bookcase", ItemKind.Furniture, FurnitureStack),
        new(FloorLamp, "floor_lamp", ItemKind.Furniture, FurnitureStack),
        new(HousePlant, "house_plant", ItemKind.Furniture, FurnitureStack),
        new(ShirtRed, "shirt_red", ItemKind.Clothing, 1, wear: WearSlot.Shirt),
        new(ShirtGreen, "shirt_green", ItemKind.Clothing, 1, wear: WearSlot.Shirt),
        new(ShirtBlue, "shirt_blue", ItemKind.Clothing, 1, wear: WearSlot.Shirt),
        new(PantsBlue, "pants_blue", ItemKind.Clothing, 1, wear: WearSlot.Pants),
        new(PantsKhaki, "pants_khaki", ItemKind.Clothing, 1, wear: WearSlot.Pants),
        new(HatStraw, "hat_straw", ItemKind.Clothing, 1, wear: WearSlot.Hat),
        new(HatCap, "hat_cap", ItemKind.Clothing, 1, wear: WearSlot.Hat),
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
