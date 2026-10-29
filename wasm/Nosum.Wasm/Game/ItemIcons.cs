// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Inventory;

namespace Nosum.Wasm.Game;

/// <summary>
/// Pictures of the items, cut out of the early sheets (docs/adr/ADR-007). Addressed by sheet and
/// grid cell, so replacing the art means replacing this table, nothing else.
/// </summary>
internal sealed class ItemIcons : IItemIconSource
{
    internal const string ObjectsSheet = "assets/sprites/items.png";
    internal const string ToolsSheet = "assets/sprites/tools.png";
    internal const string WeaponsSheet = "assets/sprites/weapons.png";
    private const string FurnitureSheet = "assets/sprites/furniture.png";
    private const string CraftedSheet = "assets/sprites/craftables.png";
    private const int Cell = 16;

    private static readonly Dictionary<int, ItemIcon> s_icons = Build();

    private static Dictionary<int, ItemIcon> Build()
    {
        Dictionary<int, ItemIcon> icons = new()
        {
            [ItemCatalog.Hoe] = Tool(0, 2),
            [ItemCatalog.WateringCan] = Tool(0, 14),
            [ItemCatalog.Axe] = Tool(5, 10),
            [ItemCatalog.Pickaxe] = Tool(5, 6),
            [ItemCatalog.Scythe] = Weapon(7, 5),

            // Upgraded tools: tools.png keeps every tier of a tool in the same row, copper seven columns and steel fourteen columns to the right of the basic one.
            [ItemCatalog.CopperHoe] = Tool(7, 2),
            [ItemCatalog.CopperWateringCan] = Tool(7, 14),
            [ItemCatalog.CopperAxe] = Tool(12, 10),
            [ItemCatalog.CopperPickaxe] = Tool(12, 6),
            [ItemCatalog.IronHoe] = Tool(14, 2),
            [ItemCatalog.IronWateringCan] = Tool(14, 14),
            [ItemCatalog.IronAxe] = Tool(19, 10),
            [ItemCatalog.IronPickaxe] = Tool(19, 6),
            [ItemCatalog.WoodenSword] = Weapon(0, 0),
            [ItemCatalog.BronzeSword] = Weapon(1, 0),
            [ItemCatalog.IronSword] = Weapon(2, 0),
            [ItemCatalog.SteelSword] = Weapon(3, 0),
            [ItemCatalog.GoldSword] = Weapon(4, 0),
            [ItemCatalog.ArcaneStaff] = Weapon(5, 0),
            [ItemCatalog.MilkPail] = Tool(6, 0),
            // Blueprints are the rolled plan at the start of the tools sheet, both buildings alike (early art, docs/adr/ADR-007).
            [ItemCatalog.CoopBlueprint] = Tool(0, 0),
            [ItemCatalog.BarnBlueprint] = Tool(0, 0),
            [ItemCatalog.OakSeed] = ObjectId(22),
            [ItemCatalog.MapleSeed] = ObjectId(23),
            [ItemCatalog.PineSeed] = ObjectId(24),
            [ItemCatalog.Wood] = ObjectId(46),
            [ItemCatalog.Stone] = ObjectId(47),
            [ItemCatalog.Fiber] = ObjectId(48),
            [ItemCatalog.Coal] = ObjectId(49),
            [ItemCatalog.CopperOre] = ObjectId(50),
            [ItemCatalog.IronOre] = ObjectId(51),
            [ItemCatalog.CopperBar] = ObjectId(52),
            [ItemCatalog.IronBar] = ObjectId(53),
            [ItemCatalog.Chest] = Craftable(32, 528),
            [ItemCatalog.Scarecrow] = Craftable(0, 32),
            [ItemCatalog.Guestbook] = Craftable(0, 32),
            [ItemCatalog.Furnace] = Craftable(80, 40),
            [ItemCatalog.WoodFence] = ObjectId(54),
            [ItemCatalog.StonePath] = ObjectId(55),
            [ItemCatalog.Sprinkler] = ObjectId(56),
            [ItemCatalog.Salad] = ObjectId(62),
            [ItemCatalog.BakedPotato] = ObjectId(63),
            [ItemCatalog.PumpkinSoup] = ObjectId(64),
            [ItemCatalog.OakChair] = Furniture(0, 16),
            [ItemCatalog.BlueArmchair] = Furniture(8, 160),
            [ItemCatalog.BlueCouch] = Furniture(16, 216),
            [ItemCatalog.OakTable] = Furniture(8, 576),
            [ItemCatalog.Bookcase] = Furniture(56, 656),
            [ItemCatalog.FloorLamp] = Furniture(48, 733),
            [ItemCatalog.HousePlant] = Furniture(0, 688),
            [ItemCatalog.ShirtRed] = Shirt(1),
            [ItemCatalog.ShirtGreen] = Shirt(11),
            [ItemCatalog.ShirtBlue] = Shirt(7),
            [ItemCatalog.PantsBlue] = Pants("item-pants-blue"),
            [ItemCatalog.PantsKhaki] = Pants("item-pants-khaki"),
            [ItemCatalog.HatStraw] = Hat(3),
            [ItemCatalog.HatCap] = Hat(5),
        };

        // Produce, seeds and forage name their own cell of the objects sheet in the catalogue (docs/adr/ADR-025 D6).
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            if (item.ObjectIcon > 0 && !icons.ContainsKey(item.Id))
            {
                icons.Add(item.Id, ObjectId(item.ObjectIcon));
            }
        }

        return icons;
    }

    /// <summary>The sheet ground items are drawn from: the objects sheet, where produce and materials live.</summary>
    internal const string GroundSheetUrl = ObjectsSheet;

    /// <inheritdoc/>
    public bool TryGet(int itemId, out ItemIcon icon) => s_icons.TryGetValue(itemId, out icon!);

    /// <summary>Looks up the sprite cell of an item without allocating.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="icon">Icon cell when found.</param>
    /// <returns><see langword="true"/> when the item has an icon entry.</returns>
    internal static bool TryGetIcon(int itemId, out ItemIcon icon) => s_icons.TryGetValue(itemId, out icon!);

    /// <summary>Finds the cell of an item on the objects sheet, for drawing it lying on the ground; items pictured on other sheets (tools, clothes, furniture) have none.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="x">Left edge of the 16 px cell, pixels.</param>
    /// <param name="y">Top edge of the cell, pixels.</param>
    /// <returns><see langword="true"/> when the item has a picture on the objects sheet.</returns>
    internal static bool TryGetGroundCell(int itemId, out int x, out int y)
    {
        if (s_icons.TryGetValue(itemId, out ItemIcon? icon) && string.Equals(icon.SheetUrl, ObjectsSheet, System.StringComparison.Ordinal))
        {
            x = icon.X;
            y = icon.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    // items.png is 384 x 80 (24 x 5 cells). Object ids are cell numbers in compact order from 1 (0 means no picture).
    private static ItemIcon Object(int column, int row) => new(ObjectsSheet, column * Cell, row * Cell, Cell, 384, 80);

    // Object ids are the cell number on items.png (compact order, 24 cells to a row).
    private static ItemIcon ObjectId(int id) => Object(id % 24, id / 24);

    // The scythe is a weapon: weapons.png is 128 x 144 (8 x 9 cells), the scythe is cell 47.
    private static ItemIcon Weapon(int column, int row) => new(WeaponsSheet, column * Cell, row * Cell, Cell, 128, 144);

    // Clothing is shown by its piece of the farmer sheets (the same frames the farmer is drawn with): shirts.png is 256 x 608 (8 px
    // shirt pieces), pants.png 1920 x 1376, hats.png 240 x 880 (20 px hats, 12 per row, four rows of directions each).
    // A shirt piece is only 8 px, so it is shown at twice its size (the crop and the sheet both doubled: a 16 px window onto a 512 x 1216 picture of the sheet).
    private static ItemIcon Shirt(int index) => new("assets/farmer/shirts.png", index % 16 * 8 * 2, index / 16 * 32 * 2, 16, 256 * 2, 608 * 2);

    // The pants frames of the sheet are 8 x 5 px grey hips, too small to read as an item: the theme draws a 16 x 16 pair of trousers itself,
    // in the item's colour (nosum-theme.js ITEM_ART).
    private static ItemIcon Pants(string themeImage) => new(string.Empty, 0, 0, 16, 16, 16, themeImage);

    private static ItemIcon Hat(int index) => new("assets/farmer/hats.png", index % 12 * 20, index / 12 * 80, 20, 240, 880);

    // tools.png is 336 x 384 (21 x 24 cells).
    private static ItemIcon Tool(int column, int row) => new(ToolsSheet, column * Cell, row * Cell, Cell, 336, 384);

    // furniture.png is 512 x 1488; a furniture icon is a 16 px crop of the piece (early art, docs/adr/ADR-007).
    private static ItemIcon Furniture(int x, int y) => new(FurnitureSheet, x, y, Cell, 512, 1488);

    // craftables.png is 128 x 1472 (8 columns of 16 x 32 cells); an icon is a 16 px square of the cell, cut where the object reads best (chest: the body, scarecrow: the head, furnace: the middle).
    private static ItemIcon Craftable(int x, int y) => new(CraftedSheet, x, y, Cell, 128, 1472);
}
