// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Core;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws a layered farmer (body, pants, shirt, arms, hair) from the farmer sheets
/// (docs/adr/ADR-007 D4). Frames are 16 × 32; the feet sit on the frame's bottom edge.
/// </summary>
internal static class FarmerSprite
{
    /// <summary>Frame width in pixels.</summary>
    public const int Width = 16;

    /// <summary>Frame height in pixels.</summary>
    public const int Height = 32;

    private const int ArmsColumnOffset = 6;
    private const int PantsStyleWidth = 192;
    private const int HairOffsetY = 2;
    private const int ShirtOffsetX = 4;
    private const int ShirtOffsetY = 15;
    private const int ShirtSize = 8;

    private static readonly int[] s_walkColumns = [0, 1, 0, 2];

    /// <summary>
    /// Records the farmer's layers.
    /// </summary>
    /// <param name="commands">Where draws go.</param>
    /// <param name="textures">Texture ids of the farmer sheets.</param>
    /// <param name="look">What the farmer wears.</param>
    /// <param name="facing">Facing.</param>
    /// <param name="walkFrame">Walk cycle step (any integer; 0 when standing).</param>
    /// <param name="feetX">Feet x on screen, unscaled pixels.</param>
    /// <param name="feetY">Feet y on screen, unscaled pixels.</param>
    public static void Draw(RenderCommandBuffer commands, in FarmerTextures textures, in FarmerLook look, Facing facing, int walkFrame, int feetX, int feetY)
    {
        // Sheet rows: 0 down, 1 right, 2 up; left is right mirrored.
        int row = facing switch { Facing.Down => 0, Facing.Up => 2, _ => 1 };
        int flags = facing == Facing.Left ? RenderCommandBuffer.FlipX : 0;
        int column = s_walkColumns[walkFrame & 3];
        int x = feetX - (Width / 2);
        int y = feetY - Height;
        int frameX = column * Width;
        int frameY = row * Height;

        commands.Draw(textures.Body, frameX, frameY, Width, Height, x, y, flags);
        commands.Draw(textures.Pants, (look.Pants % 10 * PantsStyleWidth) + frameX, (look.Pants / 10 * 672) + frameY, Width, Height, x, y, flags, look.PantsTint);

        int shirtRow = facing == Facing.Up ? 24 : facing == Facing.Down ? 0 : 8;
        commands.Draw(textures.Shirts, look.Shirt % 16 * ShirtSize, (look.Shirt / 16 * 32) + shirtRow, ShirtSize, ShirtSize, x + ShirtOffsetX, y + ShirtOffsetY + (column == 0 ? 0 : 1), flags);

        commands.Draw(textures.Body, frameX + (ArmsColumnOffset * Width), frameY, Width, Height, x, y, flags);
        commands.Draw(textures.Hair, look.Hairstyle % 8 * Width, (look.Hairstyle / 8 * 96) + (row * Height), Width, Height, x, y + HairOffsetY + (column == 0 ? 0 : 1), flags, look.HairTint);
    }
}

/// <summary>Texture ids of the farmer layer sheets.</summary>
/// <param name="Body">farmer_base.png (body and arms).</param>
/// <param name="Pants">pants.png.</param>
/// <param name="Shirts">shirts.png.</param>
/// <param name="Hair">hairstyles.png.</param>
internal readonly record struct FarmerTextures(int Body, int Pants, int Shirts, int Hair);
