// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.UnitTests.Maps;
using Nosum.Wasm.ViewModels.Inventory;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The item pictures: every rectangle is a whole sprite of its sheet, on the sheet's cells, and no picture is a crop (docs/adr/ADR-007).</summary>
public sealed class ItemIconTests
{
    private const int PngHeaderLength = 24;
    private const int PngWidthOffset = 16;
    private const int PngHeightOffset = 20;

    // The cell each sheet is cut in. Furniture is not on a cell grid: its pieces are at the rectangles PlaceableCatalog names.
    private static readonly (string Sheet, int Cell)[] s_gridSheets =
    [
        (ItemIcons.ObjectsSheet, 16),
        (ItemIcons.ToolsSheet, 16),
        (ItemIcons.WeaponsSheet, 16),
        (ItemIcons.CraftedSheet, 16),
        (ItemIcons.ShirtsSheet, 16),
        (ItemIcons.HatsSheet, 20),
    ];

    [Fact]
    public void SizeOf_EverySheet_MatchesThePngFileOnDisk()
    {
        foreach (string sheet in new[] { ItemIcons.ObjectsSheet, ItemIcons.ToolsSheet, ItemIcons.WeaponsSheet, ItemIcons.FurnitureSheet, ItemIcons.CraftedSheet, ItemIcons.ShirtsSheet, ItemIcons.HatsSheet })
        {
            (int width, int height) = PngSize(sheet);
            int scale = sheet == ItemIcons.ShirtsSheet ? ItemIcons.ShirtScale : 1;

            ItemIcons.SizeOf(sheet).Should().Be((width * scale, height * scale), $"{sheet} is listed with its real pixel size");
        }
    }

    [Fact]
    public void Icons_EveryPictureRectangle_LiesInsideItsSheet()
    {
        foreach ((int itemId, ItemIcon icon) in ItemIcons.All.Where(p => p.Value.ThemeImage is null))
        {
            icon.Width.Should().BeGreaterThan(0, ItemName(itemId));
            icon.Height.Should().BeGreaterThan(0, ItemName(itemId));
            icon.X.Should().BeGreaterThanOrEqualTo(0, ItemName(itemId));
            icon.Y.Should().BeGreaterThanOrEqualTo(0, ItemName(itemId));
            (icon.X + icon.Width).Should().BeLessThanOrEqualTo(icon.SheetWidth, ItemName(itemId));
            (icon.Y + icon.Height).Should().BeLessThanOrEqualTo(icon.SheetHeight, ItemName(itemId));
        }
    }

    [Fact]
    public void Icons_PicturesOnGridSheets_StartOnTheSheetCell()
    {
        foreach ((string sheet, int cell) in s_gridSheets)
        {
            foreach ((int itemId, ItemIcon icon) in ItemIcons.All.Where(p => p.Value.SheetUrl == sheet))
            {
                (icon.X % cell).Should().Be(0, $"{ItemName(itemId)} starts on the {cell} px grid of {sheet}");
                (icon.Y % cell).Should().Be(0, $"{ItemName(itemId)} starts on the {cell} px grid of {sheet}");
            }
        }
    }

    [Fact]
    public void Icons_PlacedFurniture_ShowTheWholeFrontView()
    {
        foreach (PlaceableDefinition placeable in PlaceableCatalog.All.Where(p => p.Sheet == PlaceableCatalog.FurnitureSheet))
        {
            PlaceableView view = placeable.View(0);
            (int sheetWidth, int sheetHeight) = ItemIcons.SizeOf(ItemIcons.FurnitureSheet);

            ItemIcons.TryGetIcon(placeable.ItemId, out ItemIcon icon).Should().BeTrue($"{ItemName(placeable.ItemId)} has a picture");
            icon.Should().Be(
                new ItemIcon(ItemIcons.FurnitureSheet, view.SourceX, view.SourceY, view.Width, view.Height, sheetWidth, sheetHeight),
                $"{ItemName(placeable.ItemId)} is pictured by its whole front view, not a crop");
        }
    }

    [Fact]
    public void Icons_CraftedPieces_ShowTheWholeCell()
    {
        foreach (int itemId in new[] { ItemCatalog.Chest, ItemCatalog.Scarecrow, ItemCatalog.Furnace, ItemCatalog.Guestbook })
        {
            ItemIcons.TryGetIcon(itemId, out ItemIcon icon).Should().BeTrue(ItemName(itemId));
            icon.Width.Should().Be(16, ItemName(itemId));
            icon.Height.Should().Be(32, $"{ItemName(itemId)} is a 16 by 32 piece on craftables.png");
        }
    }

    [Fact]
    public void Icons_EveryItemDefinition_HasAPicture()
    {
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            ItemIcons.TryGetIcon(item.Id, out _).Should().BeTrue($"{item.Key} is never an empty tile");
        }
    }

    [Theory]
    [InlineData(16, 16, 1.0)]
    [InlineData(32, 32, 0.5)]
    [InlineData(16, 32, 0.5)]
    [InlineData(48, 32, 1.0 / 3)]
    [InlineData(8, 8, 1.0)]
    public void FitScale_Sprite_IsTheLargestScaleThatFitsWithoutEnlarging(int width, int height, double expected)
    {
        ItemIcon icon = new(ItemIcons.FurnitureSheet, 0, 0, width, height, 512, 1664);

        icon.FitScale.Should().BeApproximately(expected, 1e-9);
        (width * icon.FitScale).Should().BeLessThanOrEqualTo(ItemIcon.PictureSize + 1e-9);
        (height * icon.FitScale).Should().BeLessThanOrEqualTo(ItemIcon.PictureSize + 1e-9);
    }

    private static string ItemName(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? item.Key : "item " + itemId;

    // The pixel size of a PNG file, from its IHDR chunk (bytes 16 to 23, big-endian).
    private static (int Width, int Height) PngSize(string sheet)
    {
        byte[] header = new byte[PngHeaderLength];
        using (FileStream file = File.OpenRead(RepoFiles.Path(sheet.Split('/'))))
        {
            file.ReadExactly(header);
        }

        return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(PngWidthOffset)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(PngHeightOffset)));
    }
}
