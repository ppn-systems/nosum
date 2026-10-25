// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>Where an item's picture is: a square cut out of a sprite sheet. The picture is data, not wire content.</summary>
/// <param name="SheetUrl">URL of the sheet image, relative to the app root.</param>
/// <param name="X">Left edge of the square in the sheet, pixels.</param>
/// <param name="Y">Top edge of the square in the sheet, pixels.</param>
/// <param name="Size">Side of the square, pixels.</param>
/// <param name="SheetWidth">Width of the whole sheet, pixels.</param>
/// <param name="SheetHeight">Height of the whole sheet, pixels.</param>
/// <param name="ThemeImage">Name of a 16 x 16 picture the UI theme draws itself (<c>--nosum-img-&lt;name&gt;</c>, made by <c>nosum-theme.js</c>); when set the sheet fields are not used.</param>
public sealed record ItemIcon(string SheetUrl, int X, int Y, int Size, int SheetWidth, int SheetHeight, string? ThemeImage = null);

/// <summary>Finds the picture of an item. The app provides it, so art can be replaced without touching view models.</summary>
public interface IItemIconSource
{
    /// <summary>Looks up an item's picture.</summary>
    /// <param name="itemId">Item id from the item catalogue.</param>
    /// <param name="icon">The picture when there is one.</param>
    /// <returns><see langword="true"/> when the item has a picture.</returns>
    bool TryGet(int itemId, out ItemIcon icon);
}
