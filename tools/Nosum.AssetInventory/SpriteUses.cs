// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Nosum.Core;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.AssetInventory;

/// <summary>
/// The cells of the sprite sheets the client draws: item icons, the farmer's layers, crops, nodes, buildings, animals and the UI.
/// Tables kept in catalogues are read from the catalogues; tables that live in <c>wasm/</c> (internal to the browser project) are read
/// from their source file; the few that are constants of a drawing routine are repeated here, each next to the file it mirrors.
/// </summary>
internal static partial class SpriteUses
{
    private const string Objects = "Maps/springobjects.png";
    private const string Farmer = "Characters/Farmer/";
    private const int ObjectColumns = 24;

    /// <summary>Adds every sprite cell the client draws to the ledger.</summary>
    /// <param name="ledger">The ledger.</param>
    /// <param name="repoRoot">The repository root.</param>
    public static void Collect(UseLedger ledger, string repoRoot)
    {
        ItemUses(ledger, repoRoot);
        FarmerUses(ledger, repoRoot);
        CropUses(ledger);
        NodeUses(ledger);
        BuildingUses(ledger);
        AnimalUses(ledger);
        UiUses(ledger, repoRoot);
    }

    /// <summary>Reads a file of the repository.</summary>
    /// <param name="repoRoot">The repository root.</param>
    /// <param name="relative">Path with forward slashes.</param>
    /// <returns>The text.</returns>
    internal static string ReadSource(string repoRoot, string relative) => File.ReadAllText(Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    // wasm/Nosum.Wasm/Game/ItemIcons.cs: the explicit table, then the objects-sheet cell each catalogue entry names.
    private static void ItemUses(UseLedger ledger, string repoRoot)
    {
        string source = ReadSource(repoRoot, "wasm/Nosum.Wasm/Game/ItemIcons.cs");
        HashSet<string> mapped = new(StringComparer.Ordinal);
        int rows = 0;
        foreach (Match row in IconRow().Matches(source))
        {
            _ = mapped.Add(row.Groups[1].Value);
            string kind = row.Groups[2].Value;
            int a = Number(row.Groups[3]);
            int b = row.Groups[4].Success ? Number(row.Groups[4]) : 0;
            rows++;
            switch (kind)
            {
                case "Tool":
                    ledger.Use("TileSheets/tools.png", Package.Tools, "ItemIcons", a * 16, b * 16, 16, 16);
                    break;
                case "Weapon":
                    ledger.Use("TileSheets/weapons.png", Package.Tools, "ItemIcons", a * 16, b * 16, 16, 16);
                    break;
                case "ObjectId":
                    Object(ledger, Package.Items, "ItemIcons", a);
                    break;
                case "Craftable":
                    ledger.Use("TileSheets/Craftables.png", Package.Items, "ItemIcons", a, b, 16, 16);
                    break;
                case "Furniture":
                    ledger.Use("TileSheets/furniture.png", Package.Items, "ItemIcons", a, b, 16, 16);
                    break;
                case "Shirt":
                    ledger.Use(Farmer + "shirts.png", Package.Items, "ItemIcons", a % 16 * 8, a / 16 * 32, 8, 8);
                    break;
                case "Hat":
                    ledger.Use(Farmer + "hats.png", Package.Items, "ItemIcons", a % 12 * 20, a / 12 * 80, 20, 20);
                    break;
                default:
                    // Pants: a trousers picture the UI theme draws itself, no sheet cell.
                    break;
            }
        }

        if (rows == 0)
        {
            throw new InvalidDataException("wasm/Nosum.Wasm/Game/ItemIcons.cs: no `[ItemCatalog.X] = Kind(...)` rows found; the inventory tool must be updated with it.");
        }

        foreach (ItemDefinition item in ItemCatalog.All)
        {
            if (item.ObjectIcon > 0 && !mapped.Contains(ItemName(item.Id)))
            {
                Object(ledger, Package.Items, "ItemCatalog.ObjectIcon", item.ObjectIcon);
            }
        }
    }

    // wasm/Nosum.Wasm/Game/FarmerSprite.cs: rows 0 down, 1 right, 2 up; three walk columns; the arms sit six columns to the right of the body.
    private static void FarmerUses(UseLedger ledger, string repoRoot)
    {
        string palette = ReadSource(repoRoot, "wasm/Nosum.Wasm.ViewModels/Looks/LookPalette.cs");
        List<int> shirts = [.. Indices(palette, "PlainShirt = new", null), .. Indices(palette, null, "Shirt(int itemId)")];
        List<int> pants = [.. Indices(palette, "PlainPants = new", null), .. Indices(palette, null, "Pants(int itemId)")];
        List<int> hats = Indices(palette, null, "TryHat(int itemId");
        if (shirts.Count == 0 || pants.Count == 0 || hats.Count == 0)
        {
            throw new InvalidDataException("wasm/Nosum.Wasm.ViewModels/Looks/LookPalette.cs: no shirt, pants or hat sheet index found; the inventory tool must be updated with it.");
        }

        const string By = "FarmerSprite";
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                ledger.Use(Farmer + "farmer_base.png", Package.Farmer, By, column * 16, row * 32, 16, 32, frame: true);
                ledger.Use(Farmer + "farmer_base.png", Package.Farmer, By, (column + 6) * 16, row * 32, 16, 32, frame: true);
                foreach (int style in pants)
                {
                    ledger.Use(Farmer + "pants.png", Package.Farmer, By, (style % 10 * 192) + (column * 16), (style / 10 * 672) + (row * 32), 16, 32, frame: true);
                }
            }

            for (int style = 0; style < LookCatalog.HairStyleCount; style++)
            {
                ledger.Use(Farmer + "hairstyles.png", Package.Farmer, By, style % 8 * 16, (style / 8 * 96) + (row * 32), 16, 32, frame: true);
            }
        }

        foreach (int shirt in shirts)
        {
            foreach (int piece in new[] { 0, 8, 24 })
            {
                ledger.Use(Farmer + "shirts.png", Package.Farmer, By, shirt % 16 * 8, (shirt / 16 * 32) + piece, 8, 8, frame: true);
            }
        }

        foreach (int hat in hats)
        {
            for (int direction = 0; direction < 4; direction++)
            {
                ledger.Use(Farmer + "hats.png", Package.Farmer, By, hat % 12 * 20, (hat / 12 * 80) + (direction * 20), 20, 20, frame: true);
            }
        }
    }

    // wasm/Nosum.Wasm/Game/CropSprites.cs and FarmDecorator.cs: a crop row is 16 x 32 frames, two rows per 32 px band; the soil is a 4 x 4 block, dry and wet.
    private static void CropUses(UseLedger ledger)
    {
        foreach (CropDefinition crop in CropCatalog.All)
        {
            foreach (int frame in crop.SpriteFrames)
            {
                ledger.Use("TileSheets/crops.png", Package.Crops, "CropSprites", (crop.SpriteRow % 2 * 128) + (frame * 16), crop.SpriteRow / 2 * 32, 16, 32, frame: true);
            }
        }

        ledger.Use("TerrainFeatures/hoeDirt.png", Package.Crops, "FarmDecorator", 0, 0, 128, 64);
    }

    // wasm/Nosum.Wasm/Game/NodeSprites.cs: tree pieces (every growth stage), objects-sheet cells per node kind and the ladder.
    private static void NodeUses(UseLedger ledger)
    {
        foreach (string tree in new[] { "tree1_spring", "tree2_spring", "tree3_spring" })
        {
            string sheet = "TerrainFeatures/" + tree + ".png";
            ledger.Use(sheet, Package.Nodes, "NodeSprites", 0, 0, 48, 96, frame: true);
            ledger.Use(sheet, Package.Nodes, "NodeSprites", 32, 96, 16, 32, frame: true);
            ledger.Use(sheet, Package.Nodes, "NodeSprites", 0, 96, 16, 32, frame: true);
            ledger.Use(sheet, Package.Nodes, "NodeSprites", 16, 128, 16, 16, frame: true);
            ledger.Use(sheet, Package.Nodes, "NodeSprites", 32, 128, 16, 16, frame: true);
        }

        foreach (int id in NodeObjectCells)
        {
            Object(ledger, Package.Nodes, "NodeSprites", id);
        }

        ledger.Use("Maps/Mines/mine.png", Package.Nodes, "NodeSprites", 3 * 16, 4 * 16, 16, 16);
    }

    /// <summary>The objects-sheet cells NodeSprites draws; a test checks every literal of <c>AddObject</c> calls is here.</summary>
    internal static IReadOnlyList<int> NodeObjectCells { get; } = [294, 295, 450, 343, 751, 290, 313, 314, 315, 771];

    // Farm props from the map catalogue and the pieces the players place.
    private static void BuildingUses(UseLedger ledger)
    {
        foreach (MapSpec spec in GameMaps.All)
        {
            foreach (PropPlacement placement in spec.Props)
            {
                PropDefinition prop = placement.Definition;
                ledger.Use("Buildings/" + prop.Sheet + ".png", Package.Buildings, "PropDecorator", prop.SrcX, prop.SrcY, prop.Width, prop.Height);
            }
        }

        foreach (PlaceableDefinition placeable in PlaceableCatalog.All)
        {
            Package package = placeable.Kind == PlaceableKind.Building ? Package.Buildings : Package.Items;
            string sheet = PlaceableSheet(placeable.Sheet);
            for (int rotation = 0; rotation < placeable.Rotations; rotation++)
            {
                PlaceableView view = placeable.View(rotation);
                ledger.Use(sheet, package, "PlacedObjectsDecorator", view.SourceX, view.SourceY, view.Width, view.Height);
            }
        }
    }

    // wasm/Nosum.Wasm/Game/AnimalsDecorator.cs: four walk columns, rows down, right, up (left mirrors right).
    private static void AnimalUses(UseLedger ledger)
    {
        foreach (AnimalDefinition animal in AnimalCatalog.All)
        {
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    ledger.Use("Animals/" + animal.Sheet + ".png", Package.Animals, "AnimalsDecorator", column * animal.FrameWidth, row * animal.FrameHeight, animal.FrameWidth, animal.FrameHeight, frame: true);
                }
            }
        }
    }

    // wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.js: SHEETS and the SPRITES / CURSORS rows; the start screen picture from its stylesheet.
    private static void UiUses(UseLedger ledger, string repoRoot)
    {
        string project = ReadSource(repoRoot, "wasm/Nosum.Wasm.Controls/Nosum.Wasm.Controls.csproj");
        Dictionary<string, string> linked = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match include in UiInclude().Matches(project))
        {
            foreach (string item in include.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string path = item.Trim().Replace('\\', '/');
                int marker = path.IndexOf("assets/", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    linked[Path.GetFileName(path)] = path[(marker + "assets/".Length)..];
                }
            }
        }

        string script = ReadSource(repoRoot, "wasm/Nosum.Wasm.Controls/wwwroot/nosum-theme.js");
        Dictionary<string, string> sheets = new(StringComparer.Ordinal);
        foreach (Match sheet in ThemeSheet().Matches(script))
        {
            sheets[sheet.Groups[1].Value] = linked[Path.GetFileName(sheet.Groups[2].Value)];
        }

        int rows = 0;
        foreach (Match row in ThemeRow().Matches(script))
        {
            ledger.Use(sheets[row.Groups[1].Value], Package.Ui, "nosum-theme.js", Number(row.Groups[2]), Number(row.Groups[3]), Number(row.Groups[4]), Number(row.Groups[5]));
            rows++;
        }

        if (rows == 0 || sheets.Count == 0)
        {
            throw new InvalidDataException("nosum-theme.js: no sprite rows found; the inventory tool must be updated with it.");
        }

        FileEntry background = ledger.Loaded(linked["Cloudy_Ocean_BG.png"], Package.Ui, "StartScreen.razor.css");
        background.Mark(Package.Ui, 0, 0, background.Width, background.Height, frame: false);
    }

    private static void Object(UseLedger ledger, Package package, string by, int id)
        => ledger.Use(Objects, package, by, id % ObjectColumns * 16, id / ObjectColumns * 16, 16, 16);

    /// <summary>Returns the path under <c>assets/</c> of a placeable's sheet, as PlacedObjectsDecorator.UrlOf resolves it.</summary>
    /// <param name="sheet">The sheet name in <see cref="PlaceableCatalog"/>.</param>
    /// <returns>The path.</returns>
    internal static string PlaceableSheet(string sheet)
        => string.Equals(sheet, PlaceableCatalog.ObjectsSheet, StringComparison.Ordinal) ? Objects
        : sheet.StartsWith("Buildings/", StringComparison.Ordinal) ? sheet + ".png"
        : "TileSheets/" + sheet + ".png";

    private static string ItemName(int id)
    {
        foreach (FieldInfo field in typeof(ItemCatalog).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(int) && (int)field.GetRawConstantValue()! == id)
            {
                return field.Name;
            }
        }

        return string.Empty;
    }

    // The sheet indices a LookPalette method names: the first number of each `new(N, tint)` or `new ClothingSprite(N, tint)`, in the method named, or on the line that starts with the marker.
    private static List<int> Indices(string source, string? lineMarker, string? method)
    {
        string scope;
        if (method is not null)
        {
            int start = source.IndexOf(method, StringComparison.Ordinal);
            if (start < 0)
            {
                return [];
            }

            int end = source.IndexOf("public static", start + method.Length, StringComparison.Ordinal);
            scope = end < 0 ? source[start..] : source[start..end];
        }
        else
        {
            int start = source.IndexOf(lineMarker!, StringComparison.Ordinal);
            if (start < 0)
            {
                return [];
            }

            scope = source[start..source.IndexOf(';', start)];
        }

        List<int> result = [];
        foreach (Match match in SheetIndex().Matches(scope))
        {
            result.Add(Number(match.Groups[1]));
        }

        return result;
    }

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[ItemCatalog\.(\w+)\] = (Tool|Weapon|ObjectId|Craftable|Furniture|Shirt|Hat|Pants)\((\d+)(?:, (\d+))?\)")]
    private static partial Regex IconRow();

    [GeneratedRegex(@"new(?: ClothingSprite)?\((\d+), ")]
    private static partial Regex SheetIndex();

    [GeneratedRegex("""<Content Include="([^"]+)"[^>]*wwwroot\\ui\\""")]
    private static partial Regex UiInclude();

    [GeneratedRegex("""^\s{4}(\w+): "([^"]+)",""", RegexOptions.Multiline)]
    private static partial Regex ThemeSheet();

    [GeneratedRegex("""\["[\w-]+", "(\w+)", (\d+), (\d+), (\d+), (\d+)""")]
    private static partial Regex ThemeRow();
}
