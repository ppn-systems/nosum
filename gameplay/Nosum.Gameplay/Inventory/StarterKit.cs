// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Inventory;

/// <summary>
/// What every new player's inventory starts with: the five tools and some seeds, nothing else. Furniture and clothing are bought at the town shop
/// (docs/adr/ADR-013 amendments). Counts are a product default, not a rule.
/// </summary>
public static class StarterKit
{
    /// <summary>Gets the starting items in the order they are added (hotbar order: hoe, watering can, the seeds, then axe, pickaxe and scythe; new tools come last so the slots of the original kit never move).</summary>
    public static readonly ItemSlot[] Items =
    [
        new(ItemCatalog.Hoe, 1),
        new(ItemCatalog.WateringCan, 1),
        new(ItemCatalog.ParsnipSeeds, 8),
        new(ItemCatalog.RadishSeeds, 6),
        new(ItemCatalog.CauliflowerSeeds, 4),
        new(ItemCatalog.Axe, 1),
        new(ItemCatalog.Pickaxe, 1),
        new(ItemCatalog.Scythe, 1),
    ];

    /// <summary>Checks whether the inventory already holds any tier of the given tool family (so an upgraded tool does not cause a basic duplicate to be re-issued).</summary>
    /// <param name="grid">The player's inventory grid.</param>
    /// <param name="tool">The tool family to look for.</param>
    /// <returns><see langword="true"/> when at least one item of <paramref name="tool"/> is present in <paramref name="grid"/>.</returns>
    public static bool HasToolFamily(InventoryGrid grid, ToolKind tool)
    {
        System.ArgumentNullException.ThrowIfNull(grid);
        if (tool == ToolKind.None)
        {
            return false;
        }

        for (int slot = 0; slot < grid.Capacity; slot++)
        {
            ItemSlot entry = grid[slot];
            if (!entry.IsEmpty && ItemCatalog.TryGet(entry.ItemId, out ItemDefinition? def) && def.Tool == tool)
            {
                return true;
            }
        }

        return false;
    }
}
