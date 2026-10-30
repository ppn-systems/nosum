// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>
/// Where an item's picture is: the whole sprite as a rectangle of a sheet, never a crop of it. A slot draws the rectangle scaled down to fit its
/// picture (<see cref="FitScale"/>) and centred. The picture is data, not wire content.
/// </summary>
/// <param name="SheetUrl">URL of the sheet image, relative to the app root.</param>
/// <param name="X">Left edge of the sprite in the sheet, pixels.</param>
/// <param name="Y">Top edge of the sprite in the sheet, pixels.</param>
/// <param name="Width">Width of the sprite, pixels.</param>
/// <param name="Height">Height of the sprite, pixels.</param>
/// <param name="SheetWidth">Width of the whole sheet, pixels.</param>
/// <param name="SheetHeight">Height of the whole sheet, pixels.</param>
/// <param name="ThemeImage">Name of a 16 x 16 picture the UI theme draws itself (<c>--nosum-img-&lt;name&gt;</c>, made by <c>nosum-theme.js</c>); when set the sheet fields are not used.</param>
public sealed record ItemIcon(string SheetUrl, int X, int Y, int Width, int Height, int SheetWidth, int SheetHeight, string? ThemeImage = null)
{
    /// <summary>The side of the square picture a slot shows, in UI pixels.</summary>
    public const int PictureSize = 16;

    /// <summary>
    /// Gets the scale that fits the sprite in the picture: 1 for a sprite that already fits (no enlargement, so pixel art stays crisp), otherwise the
    /// largest scale at which both sides fit. A 32 px piece is drawn at one half; a 48 by 32 couch at one third.
    /// </summary>
    public double FitScale => Math.Min(1.0, Math.Min((double)PictureSize / this.Width, (double)PictureSize / this.Height));
}

/// <summary>Finds the picture of an item. The app provides it, so art can be replaced without touching view models.</summary>
public interface IItemIconSource
{
    /// <summary>Looks up an item's picture.</summary>
    /// <param name="itemId">Item id from the item catalogue.</param>
    /// <param name="icon">The picture when there is one.</param>
    /// <returns><see langword="true"/> when the item has a picture.</returns>
    bool TryGet(int itemId, out ItemIcon icon);
}
