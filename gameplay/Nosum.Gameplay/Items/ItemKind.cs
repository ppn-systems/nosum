// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Items;

/// <summary>What an item is for. Drives what the farm does when a player uses it.</summary>
public enum ItemKind
{
    /// <summary>A tool; its <see cref="ToolKind"/> says which.</summary>
    Tool = 1,

    /// <summary>A seed; planting it grows the crop named by <see cref="ItemDefinition.CropId"/>.</summary>
    Seed = 2,

    /// <summary>Produce harvested from a crop.</summary>
    Produce = 3,

    /// <summary>Furniture a player places in their farmhouse (a <c>PlaceableDefinition</c> with the same item id).</summary>
    Furniture = 4,

    /// <summary>Clothing a player wears; its <see cref="WearSlot"/> says where (docs/adr/ADR-014).</summary>
    Clothing = 5,

    /// <summary>A material from a resource node: wood, stone, fiber, coal, ore (docs/adr/ADR-021).</summary>
    Resource = 6,

    /// <summary>A tree seed; planted on free ground it grows into a tree over game days (<see cref="ItemDefinition.TreeVariant"/>).</summary>
    TreeSeed = 7,

    /// <summary>Cooked food; eating it restores <see cref="ItemDefinition.FoodEnergy"/> energy (docs/adr/ADR-023 D2). Ids 700-799.</summary>
    Food = 8,

    /// <summary>A crafted object a player places on the ground of their farm or co-op land: chest, fence, path, scarecrow, sprinkler, furnace (docs/adr/ADR-024).</summary>
    Placeable = 9,
}

/// <summary>Where a clothing item is worn. The numbers are also the wire values of the wardrobe packets; append-only.</summary>
public enum WearSlot : byte
{
    /// <summary>Not wearable.</summary>
    None = 0,

    /// <summary>Worn on the upper body.</summary>
    Shirt = 1,

    /// <summary>Worn on the legs.</summary>
    Pants = 2,

    /// <summary>Worn on the head.</summary>
    Hat = 3,
}

/// <summary>Which tool a <see cref="ItemKind.Tool"/> item is.</summary>
public enum ToolKind
{
    /// <summary>Not a tool.</summary>
    None = 0,

    /// <summary>Tills diggable ground.</summary>
    Hoe = 1,

    /// <summary>Waters tilled soil.</summary>
    WateringCan = 2,

    /// <summary>Chops trees, stumps and twigs.</summary>
    Axe = 3,

    /// <summary>Breaks stones and ore nodes; clears empty tilled soil.</summary>
    Pickaxe = 4,

    /// <summary>Cuts weeds and grass; harvests a mature crop.</summary>
    Scythe = 5,

    /// <summary>Collects milk from a cow (docs/adr/ADR-028); costs no energy.</summary>
    MilkPail = 6,

    /// <summary>Strikes a nearby monster with a melee slash (docs/adr/ADR-045).</summary>
    Sword = 7,

    /// <summary>Casts an arcane bolt at a monster using mana (docs/adr/ADR-045).</summary>
    Staff = 8,

    /// <summary>Casts a line at water (docs/adr/ADR-078). Used with the fishing cast packets, not the farm's use-item intent.</summary>
    FishingRod = 9,
}
