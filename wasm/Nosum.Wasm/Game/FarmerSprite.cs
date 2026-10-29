// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Items;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Inventory;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws a layered farmer (body, pants, shirt, arms, hair, hat, held item/tool/weapon, and active swing/skill VFX)
/// from the farmer and item sheets (docs/adr/ADR-007 D4). Frames are 16 × 32; the feet sit on the frame's bottom edge.
/// </summary>
internal static partial class FarmerSprite
{
    /// <summary>Frame width in pixels.</summary>
    public const int Width = 16;

    /// <summary>Frame height in pixels.</summary>
    public const int Height = 32;

    private const int ArmsColumnOffset = 6;
    private const int ActionColumn = 3;
    private const int PantsStyleWidth = 192;
    private const int HairOffsetY = 2;
    private const int ShirtOffsetX = 4;
    private const int ShirtOffsetY = 15;
    private const int ShirtSize = 8;
    private const int HatSize = 20;
    private const int HatOffsetY = 0;

    private static readonly int[] s_walkColumns = [4, 1, 1, 0, 5, 2, 2, 0];

    /// <summary>
    /// Adds the farmer's layers (and optionally their held item and tool/weapon swing animation) to the world batch, sorted by the feet.
    /// </summary>
    /// <param name="batch">The world sprite batch.</param>
    /// <param name="textures">Texture ids of the farmer and item sheets.</param>
    /// <param name="look">What the farmer wears.</param>
    /// <param name="facing">Facing.</param>
    /// <param name="walkFrame">Walk cycle step (any integer; 0 when standing).</param>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="id">Stable sort id of this character.</param>
    /// <param name="heldItemId">Item id currently held in hand, or 0 for empty hands.</param>
    /// <param name="swingProgress">Active tool/weapon swing progress in <c>[0, 1)</c>, or negative when not swinging.</param>
    /// <param name="exhausted">Whether the farmer is exhausted: a sweat bubble shows above the head (docs/adr/ADR-072 D4).</param>
    public static void Draw(
        WorldSpriteBatch batch,
        in FarmerTextures textures,
        in FarmerLook look,
        Facing facing,
        int walkFrame,
        int feetX,
        int feetY,
        int id = 0,
        int heldItemId = 0,
        float swingProgress = -1f,
        bool exhausted = false)
    {
        // Sheet rows: 0 down, 1 right, 2 up; left is right mirrored.
        int row = facing switch { Facing.Down => 0, Facing.Up => 2, _ => 1 };
        int flags = facing == Facing.Left ? RenderCommandBuffer.FlipX : 0;
        bool swinging = swingProgress is >= 0f and < 1f;
        int column = swinging
            ? ActionColumn
            : (walkFrame <= 0 ? 0 : s_walkColumns[(walkFrame - 1) & 7]);
        int bob = column is 1 or 2 or 3 ? 1 : 0;
        int lungeX = swinging && swingProgress is >= 0.25f and < 0.75f
            ? (facing == Facing.Right ? 1 : facing == Facing.Left ? -1 : 0)
            : 0;
        int lungeY = swinging && swingProgress is >= 0.25f and < 0.75f
            ? (facing == Facing.Down ? 1 : facing == Facing.Up ? -1 : 0)
            : 0;
        int x = feetX - (Width / 2) + lungeX;
        int y = feetY - Height + lungeY;
        int frameX = column * Width;
        int frameY = row * Height;
        int sortY = feetY;

        if (column is 1 or 2)
        {
            DrawFootstepDust(batch, facing, column, feetX, feetY, sortY - 1, id);
        }

        if (facing == Facing.Up && (heldItemId > 0 || swinging))
        {
            DrawHeldItemAndSwing(batch, textures, facing, column, bob, x, y, feetX, feetY, sortY - 1, id, heldItemId, swingProgress);
        }

        batch.Add(textures.Body, frameX, frameY, Width, Height, x, y, sortY, feetX, id, flags, look.SkinTint);
        batch.Add(textures.Pants, (look.Pants % 10 * PantsStyleWidth) + frameX, (look.Pants / 10 * 672) + frameY, Width, Height, x, y, sortY, feetX, id, flags, look.PantsTint);

        int shirtRow = facing == Facing.Up ? 24 : facing == Facing.Down ? 0 : 8;
        batch.Add(textures.Shirts, look.Shirt % 16 * ShirtSize, (look.Shirt / 16 * 32) + shirtRow, ShirtSize, ShirtSize, x + ShirtOffsetX, y + ShirtOffsetY + bob, sortY, feetX, id, flags, look.ShirtTint);

        batch.Add(textures.Body, frameX + (ArmsColumnOffset * Width), frameY, Width, Height, x, y, sortY, feetX, id, flags, RenderCommandBuffer.NoTint);
        batch.Add(textures.Hair, look.Hairstyle % 8 * Width, (look.Hairstyle / 8 * 96) + (row * Height), Width, Height, x, y + HairOffsetY + bob, sortY, feetX, id, flags, look.HairTint);

        if (look.Hat >= 0)
        {
            // hats.png: 20 x 20 frames, one hat per 20 x 80 column strip (down, right, left, up), 12 hats per row.
            int hatRow = facing switch { Facing.Down => 0, Facing.Right => 1, Facing.Left => 2, _ => 3 };
            batch.Add(textures.Hats, look.Hat % 12 * HatSize, (look.Hat / 12 * HatSize * 4) + (hatRow * HatSize), HatSize, HatSize, x - ((HatSize - Width) / 2), y + HatOffsetY + bob, sortY, feetX, id, 0, look.HatTint);
        }

        if (facing != Facing.Up && (heldItemId > 0 || swinging))
        {
            DrawHeldItemAndSwing(batch, textures, facing, column, bob, x, y, feetX, feetY, sortY + 1, id, heldItemId, swingProgress);
        }

        if (exhausted)
        {
            DrawSweatBubble(batch, feetX, feetY, sortY, id);
        }
    }

    private static void DrawFootstepDust(
        WorldSpriteBatch batch,
        Facing facing,
        int column,
        int feetX,
        int feetY,
        int sortY,
        int id)
    {
        int dustLight = unchecked((int)0xd7ccc8cc);
        int dustWarm = unchecked((int)0xbcaaa4b0);
        int side = column == 1 ? -3 : 3;
        (int dx, int dy) = facing switch
        {
            Facing.Left => (5, -1),
            Facing.Right => (-5, -1),
            Facing.Up => (side, 2),
            _ => (side, -2),
        };

        batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, feetX + dx - 1, feetY + dy - 1, sortY, feetX, id + 80, tint: dustLight);
        batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 1, 1, feetX + dx + (facing == Facing.Left ? 2 : -2), feetY + dy - 2, sortY, feetX, id + 81, tint: dustWarm);
    }

    private static void DrawHeldItemAndSwing(
        WorldSpriteBatch batch,
        in FarmerTextures textures,
        Facing facing,
        int column,
        int bob,
        int bodyX,
        int bodyY,
        int feetX,
        int feetY,
        int sortY,
        int id,
        int heldItemId,
        float swingProgress)
    {
        bool swinging = swingProgress is >= 0f and < 1f;
        int sheetTex = 0;
        int srcX = 0;
        int srcY = 0;
        int srcSize = 16;

        if (heldItemId > 0 && ItemIcons.TryGetIcon(heldItemId, out ItemIcon icon))
        {
            sheetTex = ResolveItemSheetTexture(textures, icon.SheetUrl);
            srcX = icon.X;
            srcY = icon.Y;
            srcSize = icon.Size > 0 ? icon.Size : 16;
        }

        if (sheetTex > 0)
        {
            ToolKind heldTool = ToolKind.None;
            if (ItemCatalog.TryGet(heldItemId, out ItemDefinition? heldDef))
            {
                heldTool = heldDef.Tool;
            }

            int itemFlags = facing == Facing.Left ? RenderCommandBuffer.FlipX : 0;
            int drawX;
            int drawY;
            int deg = 0;

            if (sheetTex == textures.Items || heldItemId is ItemCatalog.CoopBlueprint or ItemCatalog.BarnBlueprint or ItemCatalog.MilkPail)
            {
                int sway = column switch { 1 or 4 => -1, 2 or 5 => 1, _ => 0 };
                (drawX, drawY) = facing switch
                {
                    Facing.Down => (bodyX + 9, bodyY + 14 + bob),
                    Facing.Up => (bodyX - 6, bodyY + 10 + bob),
                    Facing.Right => (bodyX + 8 + sway, bodyY + 12 + bob),
                    _ => (bodyX - 8 - sway, bodyY + 12 + bob),
                };
            }
            else if (heldTool == ToolKind.WateringCan)
            {
                float tilt = swinging ? MathF.Sin(Math.Clamp(swingProgress, 0f, 1f) * MathF.PI) : 0f;
                int sway = swinging ? 0 : (column switch { 1 or 4 => -1, 2 or 5 => 1, _ => 0 });
                (int handX, int handY, int rawDeg) = facing switch
                {
                    Facing.Down => (bodyX + 11, bodyY + 20 + bob + (int)(tilt * 2f), 16 + (int)(tilt * 34f)),
                    Facing.Up => (bodyX + 11, bodyY + 15 + bob - (int)(tilt * 2f), -12 - (int)(tilt * 26f)),
                    Facing.Right => (bodyX + 11 + sway + (int)(tilt * 2f), bodyY + 19 + bob + (int)(tilt * 2f), 8 + (int)(tilt * 36f)),
                    _ => (bodyX + 5 - sway - (int)(tilt * 2f), bodyY + 19 + bob + (int)(tilt * 2f), -8 - (int)(tilt * 36f)),
                };
                deg = rawDeg;
                float vx = (itemFlags & RenderCommandBuffer.FlipX) != 0 ? 6f : -6f;
                float vy = 2f;
                PlaceByGrip(handX, handY, vx, vy, deg, out drawX, out drawY);
            }
            else
            {
                bool verticalSprite = heldItemId is ItemCatalog.WoodenSword or ItemCatalog.ArcaneStaff;
                int baseOffset = verticalSprite ? ((itemFlags & RenderCommandBuffer.FlipX) != 0 ? -45 : 45) : 0;
                float vx = verticalSprite ? 0f : ((itemFlags & RenderCommandBuffer.FlipX) != 0 ? 5f : -5f);
                float vy = 5f;
                int handX;
                int handY;
                int swingDeg;

                if (!swinging)
                {
                    int sway = column switch { 1 or 4 => -1, 2 or 5 => 1, _ => 0 };
                    (handX, handY, swingDeg) = facing switch
                    {
                        Facing.Down => (bodyX + 12, bodyY + 19 + bob, 22),
                        Facing.Up => (bodyX + 12, bodyY + 17 + bob, 15),
                        Facing.Right => (bodyX + 11 + sway, bodyY + 18 + bob, 16),
                        _ => (bodyX + 5 - sway, bodyY + 18 + bob, -16),
                    };
                }
                else
                {
                    // Wind-up back to the hit frame (0..HitFraction), fast strike through it (HitFraction..0.72), follow-through settle (0.72..1.0).
                    const float hit = (float)ToolPacing.HitFraction;
                    float p = Math.Clamp(swingProgress, 0f, 1f);
                    float s01 = p < hit
                        ? (p / hit) * 0.15f
                        : p < 0.72f
                            ? 0.15f + (MathF.Sin(((p - hit) / (0.72f - hit)) * (MathF.PI * 0.5f)) * 0.85f)
                            : 1f - (((p - 0.72f) / 0.28f) * 0.12f);

                    (handX, handY, swingDeg) = facing switch
                    {
                        Facing.Down => (
                            bodyX + 13 - (int)(s01 * 4f),
                            bodyY + 18 + bob + (int)(s01 * 4f),
                            (int)MathF.Round(s01 * 135f)),
                        Facing.Up => (
                            bodyX + 13 - (int)(s01 * 5f),
                            bodyY + 14 + bob - (int)(s01 * 3f),
                            15 - (int)MathF.Round(s01 * 70f)),
                        Facing.Right => (
                            bodyX + 12 + (int)(s01 * 2f),
                            bodyY + 16 + bob + (int)(s01 * 3f),
                            -15 + (int)MathF.Round(s01 * 110f)),
                        _ => (
                            bodyX + 4 - (int)(s01 * 2f),
                            bodyY + 16 + bob + (int)(s01 * 3f),
                            15 - (int)MathF.Round(s01 * 110f)),
                    };
                }

                deg = swingDeg + baseOffset;
                PlaceByGrip(handX, handY, vx, vy, deg, out drawX, out drawY);
            }

            batch.Add(
                sheetTex,
                srcX,
                srcY,
                srcSize,
                srcSize,
                drawX,
                drawY,
                sortY,
                feetX,
                id + 100,
                itemFlags | RenderCommandBuffer.RotateDegrees(deg),
                RenderCommandBuffer.NoTint);
        }

        if (swinging && swingProgress >= (float)ToolPacing.HitFraction)
        {
            DrawSwingImpactVfx(batch, facing, feetX, feetY, sortY, heldItemId, swingProgress);
        }
    }

    private static void PlaceByGrip(int handX, int handY, float vx, float vy, int deg, out int drawX, out int drawY)
    {
        float rad = deg * (MathF.PI / 180f);
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        float rx = (vx * cos) - (vy * sin);
        float ry = (vx * sin) + (vy * cos);
        drawX = (int)MathF.Round(handX - 8f - rx);
        drawY = (int)MathF.Round(handY - 8f - ry);
    }

    private static int ResolveItemSheetTexture(in FarmerTextures textures, string sheetUrl)
    {
        if (string.Equals(sheetUrl, ItemIcons.ToolsSheet, StringComparison.Ordinal))
        {
            return textures.Tools;
        }

        if (string.Equals(sheetUrl, ItemIcons.WeaponsSheet, StringComparison.Ordinal))
        {
            return textures.Weapons;
        }

        if (string.Equals(sheetUrl, ItemIcons.ObjectsSheet, StringComparison.Ordinal))
        {
            return textures.Items;
        }

        return 0;
    }
}

/// <summary>Texture ids of the farmer layer sheets and held item sheets.</summary>
/// <param name="Body">body.png (body and arms).</param>
/// <param name="Pants">pants.png.</param>
/// <param name="Shirts">shirts.png.</param>
/// <param name="Hair">hair.png.</param>
/// <param name="Hats">hats.png.</param>
/// <param name="Tools">tools.png for held tools.</param>
/// <param name="Weapons">weapons.png for held weapons and scythe.</param>
/// <param name="Items">items.png for held seeds, produce, and materials.</param>
internal readonly record struct FarmerTextures(
    int Body,
    int Pants,
    int Shirts,
    int Hair,
    int Hats,
    int Tools = 0,
    int Weapons = 0,
    int Items = 0);
