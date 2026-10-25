// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;

namespace Nosum.Wasm.ViewModels.Looks;

/// <summary>What the server's ids for one clothing item mean on the placeholder sheets (docs/adr/ADR-006 D6, ADR-007).</summary>
/// <param name="SheetIndex">Frame index in the item's sheet (<c>shirts.png</c>, <c>pants.png</c> or <c>hats.png</c>).</param>
/// <param name="Tint">Colour multiplied onto the frame, <c>0xRRGGBBAA</c>; white (<c>0xFFFFFFFF</c>) for none.</param>
public readonly record struct ClothingSprite(int SheetIndex, int Tint);

/// <summary>
/// The client's catalogue of looks: turns the ids the server sends (skin, hair style, hair dye, worn items) into sheet frames and
/// tints. The server never sends a colour or a coordinate, so replacing the art means replacing this table, nothing else
/// (docs/adr/ADR-006 D6). Colours are <c>0xRRGGBBAA</c> as the renderer takes them.
/// </summary>
public static class LookPalette
{
    private const int White = unchecked((int)0xFFFFFFFF);

    // Skin tones multiply onto the base body.
    private static readonly int[] s_skin =
    [
        White,
        unchecked((int)0xE6B8A0FF),
        unchecked((int)0xB88B6BFF),
        unchecked((int)0x8A5E45FF),
    ];

    private static readonly int[] s_dye =
    [
        unchecked((int)0x6B4226FF),
        unchecked((int)0xD9B25CFF),
        unchecked((int)0x2B2B2BFF),
        unchecked((int)0xB5472EFF),
        unchecked((int)0x8F8F8FFF),
        unchecked((int)0x4A6FB5FF),
    ];

    /// <summary>Gets the shirt frame drawn when a player wears no shirt item.</summary>
    public static readonly ClothingSprite PlainShirt = new(3, White);

    /// <summary>Gets the trousers drawn when a player wears no trousers item.</summary>
    public static readonly ClothingSprite PlainPants = new(0, unchecked((int)0x7A6A55FF));

    /// <summary>Returns the body tint of a skin tone.</summary>
    /// <param name="skinId">Skin tone id; an unknown id gives the first tone.</param>
    /// <returns>The tint.</returns>
    public static int SkinTint(int skinId) => s_skin[(uint)(skinId - 1) < (uint)s_skin.Length ? skinId - 1 : 0];

    /// <summary>Returns the hair tint of a dye.</summary>
    /// <param name="dyeId">Hair dye id; an unknown id gives the first dye.</param>
    /// <returns>The tint.</returns>
    public static int DyeTint(int dyeId) => s_dye[(uint)(dyeId - 1) < (uint)s_dye.Length ? dyeId - 1 : 0];

    /// <summary>Returns the frame of a hair style in <c>hairstyles.png</c> (eight styles per row).</summary>
    /// <param name="styleId">Hair style id, from 1.</param>
    /// <returns>The zero-based frame index.</returns>
    public static int HairFrame(int styleId) => Math.Clamp(styleId - 1, 0, LookCatalog.HairStyleCount - 1);

    /// <summary>Returns the shirt to draw.</summary>
    /// <param name="itemId">The worn shirt item id, or zero.</param>
    /// <returns>Frame and tint.</returns>
    public static ClothingSprite Shirt(int itemId) => itemId switch
    {
        ItemCatalog.ShirtRed => new(1, White),
        ItemCatalog.ShirtGreen => new(11, White),
        ItemCatalog.ShirtBlue => new(7, White),
        _ => PlainShirt,
    };

    /// <summary>Returns the trousers to draw.</summary>
    /// <param name="itemId">The worn trousers item id, or zero.</param>
    /// <returns>Frame and tint.</returns>
    public static ClothingSprite Pants(int itemId) => itemId switch
    {
        ItemCatalog.PantsBlue => new(0, unchecked((int)0x3E5F8AFF)),
        ItemCatalog.PantsKhaki => new(0, unchecked((int)0xC2B280FF)),
        _ => PlainPants,
    };

    /// <summary>Returns the hat to draw.</summary>
    /// <param name="itemId">The worn hat item id, or zero.</param>
    /// <param name="sprite">The frame and tint when a hat is worn.</param>
    /// <returns><see langword="true"/> when a hat is drawn.</returns>
    public static bool TryHat(int itemId, out ClothingSprite sprite)
    {
        switch (itemId)
        {
            case ItemCatalog.HatStraw:
                sprite = new ClothingSprite(8, White);
                return true;
            case ItemCatalog.HatCap:
                sprite = new ClothingSprite(26, White);
                return true;
            default:
                sprite = default;
                return false;
        }
    }

    /// <summary>Formats a tint as a CSS colour for swatches in the wardrobe panel.</summary>
    /// <param name="tint">A <c>0xRRGGBBAA</c> tint.</param>
    /// <returns>For example <c>#6b4226</c>.</returns>
    public static string Css(int tint)
        => string.Create(CultureInfo.InvariantCulture, $"#{(tint >>> 24) & 0xFF:x2}{(tint >>> 16) & 0xFF:x2}{(tint >>> 8) & 0xFF:x2}");
}
