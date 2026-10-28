// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Inventory;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws a layered farmer (body, pants, shirt, arms, hair, hat, held item/tool/weapon, and active swing/skill VFX)
/// from the farmer and item sheets (docs/adr/ADR-007 D4). Frames are 16 × 32; the feet sit on the frame's bottom edge.
/// </summary>
internal static class FarmerSprite
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

    private static readonly int[] s_walkColumns = [0, 1, 0, 2];

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
        float swingProgress = -1f)
    {
        // Sheet rows: 0 down, 1 right, 2 up; left is right mirrored.
        int row = facing switch { Facing.Down => 0, Facing.Up => 2, _ => 1 };
        int flags = facing == Facing.Left ? RenderCommandBuffer.FlipX : 0;
        bool swinging = swingProgress is >= 0f and < 1f;
        int column = swinging ? ActionColumn : s_walkColumns[walkFrame & 3];
        int bob = column is 1 or 2 ? 1 : 0;
        int x = feetX - (Width / 2);
        int y = feetY - Height;
        int frameX = column * Width;
        int frameY = row * Height;
        int sortY = feetY;

        if (facing == Facing.Up && (heldItemId > 0 || swinging))
        {
            DrawHeldItemAndSwing(batch, textures, facing, bob, x, y, feetX, feetY, sortY - 1, id, heldItemId, swingProgress);
        }

        batch.Add(textures.Body, frameX, frameY, Width, Height, x, y, sortY, feetX, id, flags, look.SkinTint);
        batch.Add(textures.Pants, (look.Pants % 10 * PantsStyleWidth) + frameX, (look.Pants / 10 * 672) + frameY, Width, Height, x, y, sortY, feetX, id, flags, look.PantsTint);

        int shirtRow = facing == Facing.Up ? 24 : facing == Facing.Down ? 0 : 8;
        batch.Add(textures.Shirts, look.Shirt % 16 * ShirtSize, (look.Shirt / 16 * 32) + shirtRow, ShirtSize, ShirtSize, x + ShirtOffsetX, y + ShirtOffsetY + bob, sortY, feetX, id, flags, RenderCommandBuffer.NoTint);

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
            DrawHeldItemAndSwing(batch, textures, facing, bob, x, y, feetX, feetY, sortY + 1, id, heldItemId, swingProgress);
        }
    }

    /// <summary>
    /// Draws active combat skill visual effects (Whirlwind blade vortex, Fireball cast circle, or Divine Heal sanctuary) around the farmer.
    /// </summary>
    /// <param name="batch">The world sprite batch.</param>
    /// <param name="textures">Texture ids of the farmer and item sheets.</param>
    /// <param name="skillId">Skill id (<c>1</c> = Whirlwind, <c>2</c> = Fireball, <c>3</c> = Divine Heal).</param>
    /// <param name="progress">Normalized animation progress in <c>[0, 1)</c>.</param>
    /// <param name="feetX">Farmer's feet X in world pixels.</param>
    /// <param name="feetY">Farmer's feet Y in world pixels.</param>
    public static void DrawSkillEffect(
        WorldSpriteBatch batch,
        in FarmerTextures textures,
        byte skillId,
        float progress,
        int feetX,
        int feetY)
    {
        if (progress is < 0f or >= 1f)
        {
            return;
        }

        int centerY = feetY - 10;
        switch ((CombatSkillKind)skillId)
        {
            case CombatSkillKind.Whirlwind:
            {
                // 360-degree crimson-gold spinning blade vortex + 4 orbiting spectral blades.
                int radius = 14 + (int)(progress * 8f);
                int goldTint = unchecked((int)0xffd54fe6);
                int crimsonTint = unchecked((int)0xff5252d8);
                int whiteTint = unchecked((int)0xffffffeb);

                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, radius * 2, 3, feetX - radius, centerY - 1, feetY + 1, feetX, 9300, tint: crimsonTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, radius, feetX - 1, centerY - (radius / 2), feetY + 1, feetX, 9301, tint: crimsonTint);

                for (int i = 0; i < 4; i++)
                {
                    float angle = (progress * MathF.PI * 4f) + (i * (MathF.PI * 0.5f));
                    int ox = feetX + (int)(MathF.Cos(angle) * radius);
                    int oy = centerY + (int)(MathF.Sin(angle) * (radius * 0.65f));
                    int sortY = oy > centerY ? feetY + 2 : feetY - 2;

                    if (textures.Weapons > 0)
                    {
                        int flip = MathF.Cos(angle) < 0f ? RenderCommandBuffer.FlipX : 0;
                        batch.Add(textures.Weapons, 32, 0, 16, 16, ox - 8, oy - 8, sortY, ox, 9310 + i, flip, RenderCommandBuffer.NoTint);
                    }

                    batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, ox - 3, oy - 1, sortY, ox, 9320 + i, tint: goldTint);
                    batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, ox - 1, oy - 1, sortY, ox, 9330 + i, tint: whiteTint);
                }

                break;
            }

            case CombatSkillKind.Fireball:
            {
                // Blazing pyromancy rune ring at the feet + rising flame embers.
                int ringRadius = 10 + (int)(MathF.Sin(progress * MathF.PI) * 5f);
                int flameTint = unchecked((int)0xff5722e0);
                int emberTint = unchecked((int)0xffca28f0);

                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, ringRadius * 2, 2, feetX - ringRadius, feetY - 2, feetY - 1, feetX, 9340, tint: flameTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, ringRadius, 2, feetX - (ringRadius / 2), feetY - 6, feetY - 1, feetX, 9341, tint: emberTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, ringRadius, 2, feetX - (ringRadius / 2), feetY + 1, feetY + 1, feetX, 9342, tint: emberTint);

                for (int i = 0; i < 6; i++)
                {
                    float a = (progress * MathF.PI * 2f) + (i * (MathF.PI / 3f));
                    int ex = feetX + (int)(MathF.Cos(a) * 11f);
                    int ey = feetY - 4 - (int)(((progress * 22f) + (i * 4f)) % 24f);
                    int tint = (i & 1) == 0 ? emberTint : flameTint;
                    batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, ex - 1, ey - 1, feetY + 2, ex, 9350 + i, tint: tint);
                }

                break;
            }

            case CombatSkillKind.DivineHeal:
            {
                // Radiant emerald-gold sanctuary aura + ascending healing crosses.
                int haloW = 18 + (int)(MathF.Sin(progress * MathF.PI) * 6f);
                int emeraldTint = unchecked((int)0x69f0aeeb);
                int goldTint = unchecked((int)0xfff176f0);

                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, haloW, 2, feetX - (haloW / 2), feetY - 2, feetY - 1, feetX, 9360, tint: emeraldTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, haloW - 4, 2, feetX - ((haloW - 4) / 2), feetY + 1, feetY + 1, feetX, 9361, tint: goldTint);

                for (int i = 0; i < 4; i++)
                {
                    float phase = (progress + (i * 0.22f)) % 1f;
                    int cx = feetX + ((i switch { 0 => -10, 1 => 9, 2 => -5, _ => 6 }));
                    int cy = feetY - 6 - (int)(phase * 26f);
                    int crossTint = (i & 1) == 0 ? emeraldTint : goldTint;

                    batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 6, cx - 1, cy - 3, feetY + 2, cx, 9370 + (i * 2), tint: crossTint);
                    batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, cx - 3, cy - 1, feetY + 2, cx, 9371 + (i * 2), tint: crossTint);
                }

                break;
            }
        }
    }

    private static void DrawHeldItemAndSwing(
        WorldSpriteBatch batch,
        in FarmerTextures textures,
        Facing facing,
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
            int itemFlags = facing == Facing.Left ? RenderCommandBuffer.FlipX : 0;
            int drawX;
            int drawY;

            if (!swinging)
            {
                (drawX, drawY) = facing switch
                {
                    Facing.Down => (bodyX + 6, bodyY + 11 + bob),
                    Facing.Up => (bodyX - 5, bodyY + 8 + bob),
                    Facing.Right => (bodyX + 6, bodyY + 10 + bob),
                    _ => (bodyX - 6, bodyY + 10 + bob),
                };
            }
            else
            {
                int stage = swingProgress < 0.28f ? 0 : swingProgress < 0.68f ? 1 : 2;
                (drawX, drawY) = (facing, stage) switch
                {
                    (Facing.Down, 0) => (bodyX - 5, bodyY + 3),
                    (Facing.Down, 1) => (bodyX + 2, bodyY + 18),
                    (Facing.Down, _) => (bodyX + 7, bodyY + 20),

                    (Facing.Up, 0) => (bodyX + 5, bodyY + 9),
                    (Facing.Up, 1) => (bodyX, bodyY - 6),
                    (Facing.Up, _) => (bodyX - 6, bodyY - 4),

                    (Facing.Right, 0) => (bodyX + 2, bodyY + 2),
                    (Facing.Right, 1) => (bodyX + 12, bodyY + 12),
                    (Facing.Right, _) => (bodyX + 10, bodyY + 18),

                    (_, 0) => (bodyX - 2, bodyY + 2),
                    (_, 1) => (bodyX - 12, bodyY + 12),
                    (_, _) => (bodyX - 10, bodyY + 18),
                };
            }

            batch.Add(sheetTex, srcX, srcY, srcSize, srcSize, drawX, drawY, sortY, feetX, id + 100, itemFlags, RenderCommandBuffer.NoTint);
        }

        if (swinging && swingProgress >= 0.20f)
        {
            DrawSwingImpactVfx(batch, facing, feetX, feetY, sortY, heldItemId, swingProgress);
        }
    }

    private static void DrawSwingImpactVfx(
        WorldSpriteBatch batch,
        Facing facing,
        int feetX,
        int feetY,
        int sortY,
        int heldItemId,
        float swingProgress)
    {
        float t = Math.Clamp((swingProgress - 0.20f) / 0.80f, 0f, 1f);
        int reach = 14;
        (int tx, int ty) = facing switch
        {
            Facing.Up => (feetX, feetY - 18),
            Facing.Left => (feetX - reach, feetY - 6),
            Facing.Right => (feetX + reach, feetY - 6),
            _ => (feetX, feetY + 8),
        };

        ToolKind tool = ToolKind.None;
        if (ItemCatalog.TryGet(heldItemId, out ItemDefinition? def))
        {
            tool = def.Tool;
        }

        bool isWeapon = CombatRules.WeaponDamage(heldItemId) > 0 || tool == ToolKind.Scythe;
        if (isWeapon)
        {
            int goldSlash = unchecked((int)0xfff3b0e8);
            int emberSlash = unchecked((int)0xff9e00d8);
            int spread = (int)((t - 0.5f) * 12f);
            if (facing is Facing.Left or Facing.Right)
            {
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 14, tx - 1, ty - 7 + (spread / 2), sortY, feetX, 9200, tint: goldSlash);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 10, tx + (facing == Facing.Right ? 2 : -2), ty - 5, sortY, feetX, 9201, tint: emberSlash);
            }
            else
            {
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 14, 3, tx - 7 + (spread / 2), ty - 1, sortY, feetX, 9200, tint: goldSlash);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 10, 2, tx - 5, ty + (facing == Facing.Down ? 2 : -2), sortY, feetX, 9201, tint: emberSlash);
            }

            return;
        }

        int burst = 2 + (int)(t * 8f);
        switch (tool)
        {
            case ToolKind.WateringCan:
            {
                int waterTint = unchecked((int)0x4fc3f7ee);
                int foamTint = unchecked((int)0xb3e5fcee);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx - burst, ty - 2, sortY, feetX, 9210, tint: waterTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx + burst - 2, ty - 2, sortY, feetX, 9211, tint: waterTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - (burst / 2), ty - burst, sortY, feetX, 9212, tint: foamTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx + (burst / 2), ty + (burst / 2), sortY, feetX, 9213, tint: foamTint);
                break;
            }

            case ToolKind.Hoe:
            {
                int soilTint = unchecked((int)0x8d5524ee);
                int pebbleTint = unchecked((int)0xc68b59ee);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx - burst, ty - (burst / 2), sortY, feetX, 9220, tint: soilTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx + burst - 2, ty - (burst / 2), sortY, feetX, 9221, tint: soilTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx, ty - burst, sortY, feetX, 9222, tint: pebbleTint);
                break;
            }

            case ToolKind.Axe:
            {
                int chipTint = unchecked((int)0xd4a359ee);
                int flashTint = unchecked((int)0xffffffd8);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, tx - 3, ty - 1, sortY, feetX, 9230, tint: flashTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, tx - burst, ty - burst, sortY, feetX, 9231, tint: chipTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, tx + burst - 2, ty - (burst / 2), sortY, feetX, 9232, tint: chipTint);
                break;
            }

            case ToolKind.Pickaxe:
            {
                int sparkTint = unchecked((int)0xffd54fee);
                int emberTint = unchecked((int)0xff7043ee);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx - 1, ty - 1, sortY, feetX, 9240, tint: unchecked((int)0xffffffeb));
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - burst, ty - burst, sortY, feetX, 9241, tint: sparkTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx + burst, ty - burst, sortY, feetX, 9242, tint: sparkTint);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - (burst / 2), ty + (burst / 2), sortY, feetX, 9243, tint: emberTint);
                break;
            }

            default:
            {
                int swipeTint = unchecked((int)0xfff3b0c8);
                batch.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, tx - 3, ty - 1, sortY, feetX, 9250, tint: swipeTint);
                break;
            }
        }
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
