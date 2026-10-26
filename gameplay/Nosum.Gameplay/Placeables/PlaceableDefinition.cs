// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Placeables;

/// <summary>
/// How a placeable looks and what it occupies in one rotation: a rectangle of its sheet, optionally mirrored, and a footprint
/// of whole tiles. The sprite is drawn bottom-left aligned to the footprint, so tall pieces overhang the tiles behind them.
/// </summary>
/// <param name="SourceX">Left of the sprite on the sheet, pixels.</param>
/// <param name="SourceY">Top of the sprite on the sheet, pixels.</param>
/// <param name="Width">Sprite width, pixels.</param>
/// <param name="Height">Sprite height, pixels.</param>
/// <param name="FootprintWidth">Tiles the piece blocks horizontally.</param>
/// <param name="FootprintHeight">Tiles the piece blocks vertically (the bottom rows of the sprite).</param>
/// <param name="FlipX">Whether the sprite is drawn mirrored.</param>
public readonly record struct PlaceableView(int SourceX, int SourceY, int Width, int Height, int FootprintWidth, int FootprintHeight, bool FlipX = false);

/// <summary>What a placeable does. Furniture is decoration; the rest are the crafted objects of docs/adr/ADR-024.</summary>
public enum PlaceableKind : byte
{
    /// <summary>Decoration with no behaviour (furniture, a scarecrow until crows exist).</summary>
    Decoration = 0,

    /// <summary>A storage chest with its own inventory (36 slots).</summary>
    Chest = 1,

    /// <summary>A fence piece: blocks walking.</summary>
    Fence = 2,

    /// <summary>A path tile: decorative, walked over.</summary>
    Path = 3,

    /// <summary>A sprinkler: waters the four tiles around it each game day.</summary>
    Sprinkler = 4,

    /// <summary>A furnace: smelts ore and coal into bars over game time.</summary>
    Furnace = 5,

    /// <summary>A farm building (coop, barn): large, with a door into its own interior; placed from a blueprint, built over two game days, never moved or picked up (docs/adr/ADR-028).</summary>
    Building = 6,
}

/// <summary>
/// A thing a player may put on the ground of a placeable map: it is made from an inventory item
/// (<see cref="ItemId"/>), has one view per rotation and a footprint that blocks walking. Furniture is the first kind;
/// decorations and machines can use the same definition (docs/adr/ADR-015).
/// </summary>
public sealed class PlaceableDefinition
{
    private readonly PlaceableView[] _views;

    /// <summary>Initializes a definition.</summary>
    /// <param name="itemId">The inventory item that is consumed when it is placed and returned when it is picked up.</param>
    /// <param name="sheet">Name of the sheet the art is on (a file in <c>sprites/</c>, without extension).</param>
    /// <param name="lightRadius">Radius, in pixels, of the light the piece gives at night (docs/adr/ADR-027 D5); zero for a piece that gives none.</param>
    /// <param name="views">One view per rotation (clockwise from facing down), or a single view for a piece that does not rotate.</param>
    public PlaceableDefinition(int itemId, string sheet, int lightRadius, params PlaceableView[] views)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lightRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentException.ThrowIfNullOrEmpty(sheet);
        ArgumentNullException.ThrowIfNull(views);
        if (views.Length is not (1 or 4))
        {
            throw new ArgumentException("A placeable has one view, or four (down, right, up, left).", nameof(views));
        }

        foreach (PlaceableView view in views)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(view.FootprintWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(view.FootprintHeight);
        }

        this.ItemId = itemId;
        this.Sheet = sheet;
        this.LightRadius = lightRadius;
        _views = views;
    }

    /// <summary>Initializes a definition that gives no light.</summary>
    /// <param name="itemId">The inventory item that is consumed when it is placed and returned when it is picked up.</param>
    /// <param name="sheet">Name of the sheet the art is on.</param>
    /// <param name="views">One view per rotation, or a single view.</param>
    public PlaceableDefinition(int itemId, string sheet, params PlaceableView[] views)
        : this(itemId, sheet, 0, views)
    {
    }

    /// <summary>Gets the inventory item this is made from.</summary>
    public int ItemId { get; }

    /// <summary>Gets the sheet name (a file in <c>sprites/</c>, without extension).</summary>
    public string Sheet { get; }

    /// <summary>Gets the radius, in pixels, of the light this piece gives in the night tint; zero when it gives none.</summary>
    public int LightRadius { get; }

    /// <summary>Gets what the piece does; <see cref="PlaceableKind.Decoration"/> by default.</summary>
    public PlaceableKind Kind { get; init; }

    /// <summary>Gets whether the footprint blocks walking; a path does not, everything else does.</summary>
    public bool BlocksWalking { get; init; } = true;

    /// <summary>
    /// Gets the door tile of a building, relative to the left and top tile of its footprint, or <see langword="null"/> for a piece with no door. The door is inside the footprint but
    /// opened for walking, and the tile in front of it must stay free (docs/adr/ADR-028 D1).
    /// </summary>
    public (int X, int Y)? Door { get; init; }

    /// <summary>Gets how many rotations are valid: 1 or 4.</summary>
    public int Rotations => _views.Length;

    /// <summary>Returns the view of a rotation.</summary>
    /// <param name="rotation">Rotation, zero to <see cref="Rotations"/> minus one.</param>
    /// <returns>The view.</returns>
    public PlaceableView View(int rotation) => _views[rotation];
}
