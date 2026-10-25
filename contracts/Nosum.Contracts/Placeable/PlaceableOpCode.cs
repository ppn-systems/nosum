// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>
/// Opcodes of the Placeable domain: objects players put on the ground of their own place (furniture in the farmhouse now;
/// decorations and machines later) (docs/adr/ADR-015).
/// <para>Band <c>0xB000-0xB0FF</c> is reserved for this domain. C2S requests use <c>0xB000-0xB03F</c>,
/// S2C replies and pushes use <c>0xB040-0xB07F</c>. Crafting, chests and furnaces (docs/adr/ADR-024) use the slice <c>0xB100-0xB1FF</c>:
/// C2S <c>0xB100-0xB13F</c>, S2C <c>0xB140-0xB17F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class PlaceableOpCode
{
    // -- C2S (0xB000-0xB03F) --------------------------------------------------

    /// <summary>Asks for every placed object of the instance the player is in. Reply: <see cref="S2C.PlacedSnapshot"/>.</summary>
    public const ushort GetPlaced = 0xB000;

    /// <summary>Places the item in an inventory slot. Reply: <see cref="S2C.PlaceResult"/>; the change reaches the instance as <see cref="S2C.PlacedChanged"/>.</summary>
    public const ushort PlaceObject = 0xB001;

    /// <summary>Moves or rotates a placed object. Reply: <see cref="S2C.PlaceResult"/>.</summary>
    public const ushort MoveObject = 0xB002;

    /// <summary>Picks a placed object up into the inventory. Reply: <see cref="S2C.PlaceResult"/>.</summary>
    public const ushort PickUpObject = 0xB003;

    // -- S2C (0xB040-0xB07F) --------------------------------------------------

    /// <summary>Reply to <see cref="GetPlaced"/>.</summary>
    public const ushort PlacedSnapshot = 0xB040;

    /// <summary>Reply to <see cref="PlaceObject"/>, <see cref="MoveObject"/> and <see cref="PickUpObject"/>.</summary>
    public const ushort PlaceResult = 0xB041;

    /// <summary>Push to every player of an instance: objects were added, changed or removed.</summary>
    public const ushort PlacedChanged = 0xB042;

    // -- Crafting, chests and furnaces: C2S (0xB100-0xB13F) --------------------

    /// <summary>Crafts a recipe. Reply: <see cref="S2C.CraftResult"/>.</summary>
    public const ushort Craft = 0xB100;

    /// <summary>Asks which recipes are unlocked. Reply: <see cref="S2C.RecipeBook"/>.</summary>
    public const ushort GetRecipeBook = 0xB101;

    /// <summary>Opens a chest. Reply: <see cref="S2C.ChestSnapshot"/>.</summary>
    public const ushort OpenChest = 0xB110;

    /// <summary>Stops watching a chest. No reply.</summary>
    public const ushort CloseChest = 0xB111;

    /// <summary>Moves a stack between the inventory and an open chest. Reply: <see cref="S2C.ChestResult"/>.</summary>
    public const ushort ChestMove = 0xB112;

    /// <summary>Starts a furnace job. Reply: <see cref="S2C.FurnaceResult"/>.</summary>
    public const ushort StartSmelting = 0xB120;

    /// <summary>Collects a finished bar. Reply: <see cref="S2C.FurnaceResult"/>.</summary>
    public const ushort CollectFurnace = 0xB121;

    /// <summary>Asks for the state of the furnaces. Reply: <see cref="S2C.FurnaceSnapshot"/>.</summary>
    public const ushort GetFurnaces = 0xB122;

    // -- Crafting, chests and furnaces: S2C (0xB140-0xB17F) --------------------

    /// <summary>Reply to <see cref="Craft"/>.</summary>
    public const ushort CraftResult = 0xB140;

    /// <summary>Reply to <see cref="GetRecipeBook"/>.</summary>
    public const ushort RecipeBook = 0xB141;

    /// <summary>Reply to <see cref="OpenChest"/>.</summary>
    public const ushort ChestSnapshot = 0xB150;

    /// <summary>Reply to <see cref="ChestMove"/>.</summary>
    public const ushort ChestResult = 0xB151;

    /// <summary>Push to the viewers of a chest: slots changed.</summary>
    public const ushort ChestUpdate = 0xB152;

    /// <summary>Push to a viewer: the chest is gone or no longer theirs.</summary>
    public const ushort ChestClosed = 0xB153;

    /// <summary>Reply to <see cref="StartSmelting"/> and <see cref="CollectFurnace"/>.</summary>
    public const ushort FurnaceResult = 0xB160;

    /// <summary>Reply to <see cref="GetFurnaces"/>.</summary>
    public const ushort FurnaceSnapshot = 0xB161;

    /// <summary>Push to every player of an instance: a furnace changed state.</summary>
    public const ushort FurnaceChanged = 0xB162;
}
