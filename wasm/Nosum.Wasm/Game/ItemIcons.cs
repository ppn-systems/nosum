// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Inventory;

namespace Nosum.Wasm.Game;

/// <summary>
/// Pictures of the items, cut out of the placeholder sheets (docs/adr/ADR-007). Addressed by sheet and
/// grid cell, so replacing the art means replacing this table, nothing else.
/// </summary>
internal sealed class ItemIcons : IItemIconSource
{
    private const string ObjectsSheet = "assets/Maps/springobjects.png";
    private const string ToolsSheet = "assets/TileSheets/tools.png";
    private const string FurnitureSheet = "assets/TileSheets/furniture.png";
    private const int Cell = 16;

    private static readonly Dictionary<int, ItemIcon> s_icons = new()
    {
        [ItemCatalog.Hoe] = Tool(0, 2),
        [ItemCatalog.WateringCan] = Tool(0, 14),
        [ItemCatalog.ParsnipSeeds] = Object(16, 19),
        [ItemCatalog.RadishSeeds] = Object(4, 20),
        [ItemCatalog.CauliflowerSeeds] = Object(18, 19),
        [ItemCatalog.Parsnip] = Object(0, 1),
        [ItemCatalog.Radish] = Object(0, 11),
        [ItemCatalog.Cauliflower] = Object(22, 7),
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

    /// <inheritdoc/>
    public bool TryGet(int itemId, out ItemIcon icon) => s_icons.TryGetValue(itemId, out icon!);

    // springobjects.png is 384 x 624 (24 x 39 cells).
    private static ItemIcon Object(int column, int row) => new(ObjectsSheet, column * Cell, row * Cell, Cell, 384, 624);

    // Clothing is shown by its piece of the farmer sheets (the same frames the farmer is drawn with): shirts.png is 256 x 608 (8 px
    // shirt pieces), pants.png 1920 x 1376, hats.png 240 x 880 (20 px hats, 12 per row, four rows of directions each).
    private static ItemIcon Shirt(int index) => new("assets/Characters/Farmer/shirts.png", index % 16 * 8, index / 16 * 32, 8, 256, 608);

    // The pants frames of the sheet are 8 x 5 px grey hips, too small to read as an item: the theme draws a 16 x 16 pair of trousers itself,
    // in the item's colour (nosum-theme.js ITEM_ART).
    private static ItemIcon Pants(string themeImage) => new(string.Empty, 0, 0, 16, 16, 16, themeImage);

    private static ItemIcon Hat(int index) => new("assets/Characters/Farmer/hats.png", index % 12 * 20, index / 12 * 80, 20, 240, 880);

    // tools.png is 336 x 384 (21 x 24 cells).
    private static ItemIcon Tool(int column, int row) => new(ToolsSheet, column * Cell, row * Cell, Cell, 336, 384);

    // furniture.png is 512 x 1488; a furniture icon is a 16 px crop of the piece (placeholder art, docs/adr/ADR-007).
    private static ItemIcon Furniture(int x, int y) => new(FurnitureSheet, x, y, Cell, 512, 1488);
}
