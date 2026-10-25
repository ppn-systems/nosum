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
}
