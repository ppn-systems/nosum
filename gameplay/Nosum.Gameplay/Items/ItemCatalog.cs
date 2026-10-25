// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Farm;

namespace Nosum.Gameplay.Items;

/// <summary>
/// The generic item catalogue: every item any feature (farm, market, co-op warehouse, quests) may
/// put in an inventory. Ids are append-only. Ids below 100 are tools, 100-199 seeds, 200-299 produce, 300-399 clothing.
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

    /// <summary>A red shirt.</summary>
    public const int ShirtRed = 300;

    /// <summary>A green shirt.</summary>
    public const int ShirtGreen = 301;

    /// <summary>A blue shirt.</summary>
    public const int ShirtBlue = 302;

    /// <summary>Blue trousers.</summary>
    public const int PantsBlue = 320;

    /// <summary>Khaki trousers.</summary>
    public const int PantsKhaki = 321;

    /// <summary>A straw hat.</summary>
    public const int HatStraw = 340;

    /// <summary>A cap.</summary>
    public const int HatCap = 341;

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
