// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>
/// Pictures of the items, cut out of the sprite sheets (docs/adr/ADR-007). Every rectangle is the sprite's own: an item that is placed in the world
/// takes its picture from its placeable's front view (<see cref="PlaceableCatalog"/>), so the icon is the whole piece the world draws, never a crop of
/// it. The sheets are listed with their real pixel size; <c>ItemIconTests</c> checks every rectangle against the PNG files.
/// </summary>
public sealed class ItemIcons : IItemIconSource
{
    /// <summary>The objects sheet: produce, seeds, resources and small objects, 24 by 6 cells of 16 px.</summary>
    public const string ObjectsSheet = "assets/sprites/items.png";

    /// <summary>The tools sheet: every tier of a tool in one row, 21 by 24 cells of 16 px.</summary>
    public const string ToolsSheet = "assets/sprites/tools.png";

    /// <summary>The weapons sheet, 8 by 9 cells of 16 px.</summary>
    public const string WeaponsSheet = "assets/sprites/weapons.png";

    /// <summary>The furniture sheet: placed pieces at the rectangles <see cref="PlaceableCatalog"/> names; not on one grid.</summary>
    public const string FurnitureSheet = "assets/sprites/furniture.png";

    /// <summary>The crafted machines and containers, 16 px wide and 32 px tall pieces.</summary>
    public const string CraftedSheet = "assets/sprites/craftables.png";

    /// <summary>The farmer's shirt pieces: 8 px frames, shown at <see cref="ShirtScale"/>.</summary>
    public const string ShirtsSheet = "assets/farmer/shirts.png";

    /// <summary>The farmer's hats: 20 px frames, 12 to a row.</summary>
    public const string HatsSheet = "assets/farmer/hats.png";

    /// <summary>How much a shirt piece is enlarged before it is cut, so it reads as an item.</summary>
    public const int ShirtScale = 2;

    /// <summary>The sheet ground items are drawn from: the objects sheet, where produce and materials live.</summary>
    public const string GroundSheetUrl = ObjectsSheet;

    private const int Cell = 16;
    private const int HatCell = 20;

    private static readonly Dictionary<int, ItemIcon> s_icons = Build();

    /// <summary>Gets the real pixel size of a sheet the table cuts icons from (a shirt sheet counts at its enlarged size).</summary>
    /// <param name="sheetUrl">Sheet URL, relative to the app root.</param>
    /// <returns>The width and height in pixels; zero for a sheet the table does not use.</returns>
    public static (int Width, int Height) SizeOf(string sheetUrl) => sheetUrl switch
    {
        ObjectsSheet => (384, 96),
        ToolsSheet => (336, 384),
        WeaponsSheet => (128, 144),
        FurnitureSheet => (512, 1664),
        CraftedSheet => (128, 1472),
        ShirtsSheet => (256 * ShirtScale, 608 * ShirtScale),
        HatsSheet => (240, 880),
        _ => (0, 0),
    };

    /// <inheritdoc/>
    public bool TryGet(int itemId, out ItemIcon icon) => s_icons.TryGetValue(itemId, out icon!);

    /// <summary>Looks up the picture of an item without allocating.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="icon">The picture when found.</param>
    /// <returns><see langword="true"/> when the item has a picture.</returns>
    public static bool TryGetIcon(int itemId, out ItemIcon icon) => s_icons.TryGetValue(itemId, out icon!);

    /// <summary>Finds the cell of an item on the objects sheet, for drawing it lying on the ground; items pictured on other sheets (tools, clothes, furniture) have none.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="x">Left edge of the 16 px cell, pixels.</param>
    /// <param name="y">Top edge of the cell, pixels.</param>
    /// <returns><see langword="true"/> when the item has a picture on the objects sheet.</returns>
    public static bool TryGetGroundCell(int itemId, out int x, out int y)
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

    /// <summary>Every item that has a picture, with its picture; the unit tests walk it.</summary>
    /// <returns>A read-only view of the table.</returns>
    public static IReadOnlyDictionary<int, ItemIcon> All => s_icons;

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
            [ItemCatalog.WoodFence] = ObjectId(54),
            [ItemCatalog.StonePath] = ObjectId(55),
            [ItemCatalog.Sprinkler] = ObjectId(56),
            [ItemCatalog.Salad] = ObjectId(62),
            [ItemCatalog.BakedPotato] = ObjectId(63),
            [ItemCatalog.PumpkinSoup] = ObjectId(64),
            [ItemCatalog.ShirtRed] = Shirt(1),
            [ItemCatalog.ShirtGreen] = Shirt(11),
            [ItemCatalog.ShirtBlue] = Shirt(7),
            [ItemCatalog.PantsBlue] = Pants("item-pants-blue"),
            [ItemCatalog.PantsKhaki] = Pants("item-pants-khaki"),
            [ItemCatalog.HatStraw] = Hat(3),
            [ItemCatalog.HatCap] = Hat(5),
        };

        // Furniture, the chest, the scarecrow, the furnace and the guestbook are pictured by their placeable (docs/adr/ADR-024, ADR-073): the front view.
        foreach (PlaceableDefinition placeable in PlaceableCatalog.All)
        {
            string? sheet = SheetUrlOf(placeable.Sheet);
            if (sheet is not null && !icons.ContainsKey(placeable.ItemId))
            {
                PlaceableView view = placeable.View(0);
                icons.Add(placeable.ItemId, Cut(sheet, view.SourceX, view.SourceY, view.Width, view.Height));
            }
        }

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

    // The sheet a placeable names (PlaceableCatalog uses short names); null for a sheet the table does not cut (a building's own art).
    private static string? SheetUrlOf(string sheet) => sheet switch
    {
        PlaceableCatalog.FurnitureSheet => FurnitureSheet,
        PlaceableCatalog.CraftedSheet => CraftedSheet,
        PlaceableCatalog.ObjectsSheet => ObjectsSheet,
        _ => null,
    };

    // A rectangle of a sheet, with the sheet's size: the one place an icon is cut.
    private static ItemIcon Cut(string sheet, int x, int y, int width, int height)
    {
        (int sheetWidth, int sheetHeight) = SizeOf(sheet);
        return new ItemIcon(sheet, x, y, width, height, sheetWidth, sheetHeight);
    }

    // Object ids are the cell number on items.png (compact order, 24 cells to a row).
    private static ItemIcon ObjectId(int id) => Cut(ObjectsSheet, id % 24 * Cell, id / 24 * Cell, Cell, Cell);

    // The scythe and the swords are cells of weapons.png (8 columns of 16 px).
    private static ItemIcon Weapon(int column, int row) => Cut(WeaponsSheet, column * Cell, row * Cell, Cell, Cell);

    // Tools: one 16 px cell of tools.png.
    private static ItemIcon Tool(int column, int row) => Cut(ToolsSheet, column * Cell, row * Cell, Cell, Cell);

    // Clothing is shown by its piece of the farmer sheets: a shirt piece is 8 px and is shown at ShirtScale, so the cell is 16 px of the enlarged sheet.
    private static ItemIcon Shirt(int index) => Cut(ShirtsSheet, index % 16 * Cell, index / 16 * Cell * 4, Cell, Cell);

    // The pants frames of the sheet are 8 x 5 px grey hips, too small to read as an item: the theme draws a 16 x 16 pair of trousers itself,
    // in the item's colour (nosum-theme.js ITEM_ART).
    private static ItemIcon Pants(string themeImage) => new(string.Empty, 0, 0, Cell, Cell, Cell, Cell, themeImage);

    // A hat is a 20 px frame, 12 to a row, each row of the sheet holding four rows of directions.
    private static ItemIcon Hat(int index) => Cut(HatsSheet, index % 12 * HatCell, index / 12 * HatCell * 4, HatCell, HatCell);
}
