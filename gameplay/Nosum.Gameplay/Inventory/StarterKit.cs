// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Inventory;

/// <summary>
/// What every new player's inventory starts with: the two tools and some seeds, nothing else. Furniture and clothing are bought at the town shop
/// (docs/adr/ADR-013 amendments). Counts are a product default, not a rule.
/// </summary>
public static class StarterKit
{
    /// <summary>Gets the starting items in the order they are added (hotbar order: tools, then seeds).</summary>
    public static readonly ItemSlot[] Items =
    [
        new(ItemCatalog.Hoe, 1),
        new(ItemCatalog.WateringCan, 1),
        new(ItemCatalog.ParsnipSeeds, 8),
        new(ItemCatalog.RadishSeeds, 6),
        new(ItemCatalog.CauliflowerSeeds, 4),
    ];
}
