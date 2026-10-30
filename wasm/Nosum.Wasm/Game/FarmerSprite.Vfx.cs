// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

internal static partial class FarmerSprite
{
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
                // 360-degree crimson-gold ground vortex ring + 4 orbiting spectral blades.
                int radius = 14 + (int)(progress * 8f);
                int goldTint = unchecked((int)0xffd54fe6);
                int crimsonTint = unchecked((int)0xff5252d8);
                int whiteTint = unchecked((int)0xffffffeb);

                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, radius * 2, 2, feetX - radius, feetY - 2, feetY - 1, feetX, 9300, tint: crimsonTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, (radius * 2) - 6, 2, feetX - radius + 3, feetY + 1, feetY + 1, feetX, 9301, tint: goldTint);

                for (int i = 0; i < 4; i++)
                {
                    float angle = (progress * MathF.PI * 4f) + (i * (MathF.PI * 0.5f));
                    int ox = feetX + (int)(MathF.Cos(angle) * radius);
                    int oy = centerY + (int)(MathF.Sin(angle) * (radius * 0.65f));
                    int sortY = oy > centerY ? feetY + 2 : feetY - 2;

                    if (textures.Weapons > 0)
                    {
                        int bladeDeg = (int)MathF.Round((angle * 180f / MathF.PI) + 45f);
                        batch.AddOverlay(textures.Weapons, 32, 0, 16, 16, ox - 8, oy - 8, sortY, ox, 9310 + i, RenderCommandBuffer.RotateDegrees(bladeDeg), RenderCommandBuffer.NoTint);
                    }

                    batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, ox - 3, oy - 1, sortY, ox, 9320 + i, tint: goldTint);
                    batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, ox - 1, oy - 1, sortY, ox, 9330 + i, tint: whiteTint);
                }

                break;
            }

            case CombatSkillKind.Fireball:
            {
                // Blazing pyromancy rune ring at the feet + rising flame embers.
                int ringRadius = 10 + (int)(MathF.Sin(progress * MathF.PI) * 5f);
                int flameTint = unchecked((int)0xff5722e0);
                int emberTint = unchecked((int)0xffca28f0);

                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, ringRadius * 2, 2, feetX - ringRadius, feetY - 2, feetY - 1, feetX, 9340, tint: flameTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, ringRadius, 2, feetX - (ringRadius / 2), feetY - 6, feetY - 1, feetX, 9341, tint: emberTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, ringRadius, 2, feetX - (ringRadius / 2), feetY + 1, feetY + 1, feetX, 9342, tint: emberTint);

                for (int i = 0; i < 6; i++)
                {
                    float a = (progress * MathF.PI * 2f) + (i * (MathF.PI / 3f));
                    int ex = feetX + (int)(MathF.Cos(a) * 11f);
                    int ey = feetY - 4 - (int)(((progress * 22f) + (i * 4f)) % 24f);
                    int tint = (i & 1) == 0 ? emberTint : flameTint;
                    batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, ex - 1, ey - 1, feetY + 2, ex, 9350 + i, tint: tint);
                }

                break;
            }

            case CombatSkillKind.DivineHeal:
            {
                // Radiant emerald-gold sanctuary aura + ascending healing crosses.
                int haloW = 18 + (int)(MathF.Sin(progress * MathF.PI) * 6f);
                int emeraldTint = unchecked((int)0x69f0aeeb);
                int goldTint = unchecked((int)0xfff176f0);

                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, haloW, 2, feetX - (haloW / 2), feetY - 2, feetY - 1, feetX, 9360, tint: emeraldTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, haloW - 4, 2, feetX - ((haloW - 4) / 2), feetY + 1, feetY + 1, feetX, 9361, tint: goldTint);

                for (int i = 0; i < 4; i++)
                {
                    float phase = (progress + (i * 0.22f)) % 1f;
                    int cx = feetX + ((i switch { 0 => -10, 1 => 9, 2 => -5, _ => 6 }));
                    int cy = feetY - 6 - (int)(phase * 26f);
                    int crossTint = (i & 1) == 0 ? emeraldTint : goldTint;

                    batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 6, cx - 1, cy - 3, feetY + 2, cx, 9370 + (i * 2), tint: crossTint);
                    batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, cx - 3, cy - 1, feetY + 2, cx, 9371 + (i * 2), tint: crossTint);
                }

                break;
            }
        }
    }

    // Flecks from the tile the tool struck: dirt for the hoe, droplets for the can, chips for the axe, sparks for the pickaxe, leaves for the scythe, soil for seeds.
    // Five pixels fan out and fall while they fade; the tints are 0xRRGGBBAA and the alpha follows the hit's age.
    private static void DrawToolHitParticles(WorldSpriteBatch batch, ToolKind tool, Facing facing, int feetX, int feetY, int sortY, float hitT)
    {
        (int tx, int ty) = facing switch
        {
            Facing.Up => (feetX, feetY - 16),
            Facing.Left => (feetX - 16, feetY - 4),
            Facing.Right => (feetX + 16, feetY - 4),
            _ => (feetX, feetY + 12),
        };

        int rgb = tool switch
        {
            ToolKind.Hoe => 0x8d6e63,
            ToolKind.WateringCan => 0x64b5f6,
            ToolKind.Axe => 0xd7a86e,
            ToolKind.Pickaxe => 0xfff59d,
            ToolKind.Scythe => 0x81c784,
            _ => 0x8bc34a,
        };
        int alpha = Math.Clamp((int)((1f - hitT) * 255f), 0, 255);
        int tint = unchecked((rgb << 8) | alpha);
        float lift = MathF.Sin(hitT * MathF.PI) * 12f;
        for (int i = 0; i < 5; i++)
        {
            int px = tx + (int)((i - 2) * 5f * hitT);
            int py = ty + (int)(4f * hitT) - (int)(lift * (1f - (0.15f * i)));
            batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, px - 1, py - 1, sortY + 2, feetX, 9400 + i, tint: tint);
        }
    }

    // The sweat bubble of an exhausted farmer: a drop above the head, drawn while energy is low (docs/adr/ADR-074 D4).
    private static void DrawSweatBubble(WorldSpriteBatch batch, int feetX, int feetY, int sortY, int id)
    {
        int drop = unchecked((int)0x90caf9ff);
        int shine = unchecked((int)0xffffffff);
        int x = feetX + 9;
        int y = feetY - 30;
        batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 4, 5, x - 1, y, sortY + 2, feetX, id + 90, tint: drop);
        batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 1, 1, x, y + 1, sortY + 2, feetX, id + 91, tint: shine);
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
        if (heldItemId <= 0)
        {
            return;
        }

        ToolKind tool = ToolKind.None;
        if (ItemCatalog.TryGet(heldItemId, out ItemDefinition? def))
        {
            tool = def.Tool;
        }

        bool isWeapon = CombatRules.WeaponDamage(heldItemId) > 0 || tool == ToolKind.Scythe;
        float hitT = Math.Clamp((swingProgress - (float)ToolPacing.HitFraction) / (1f - (float)ToolPacing.HitFraction), 0f, 1f);
        if (!isWeapon)
        {
            // Farm tools and seeds: what the hit throws up from the tile (docs/adr/ADR-074 D5). Reduced motion keeps the swing and drops the flecks.
            if (!ViewOptions.ReduceMotion)
            {
                DrawToolHitParticles(batch, tool, facing, feetX, feetY, sortY, hitT);
            }

            if (tool == ToolKind.None)
            {
                return;
            }
        }

        float t = Math.Clamp((swingProgress - 0.16f) / 0.84f, 0f, 1f);
        int reach = 14;
        (int tx, int ty) = facing switch
        {
            Facing.Up => (feetX, feetY - 18),
            Facing.Left => (feetX - reach, feetY - 6),
            Facing.Right => (feetX + reach, feetY - 6),
            _ => (feetX, feetY + 8),
        };

        // Sweeping crescent motion trail along the tool/weapon swing arc
        int arcTint = unchecked((int)0xfff8e1d8);
        for (int s = 0; s < 3; s++)
        {
            int step = (s - 1) * 4;
            int ax = facing is Facing.Left or Facing.Right ? tx : tx + step;
            int ay = facing is Facing.Left or Facing.Right ? ty + step : ty;
            batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, ax - 1, ay - 1, sortY, feetX, 9190 + s, tint: arcTint);
        }

        if (isWeapon)
        {
            int goldSlash = unchecked((int)0xfff3b0f0);
            int emberSlash = unchecked((int)0xff9e00e0);
            int leafCut = unchecked((int)0x81c784e8);
            int spread = (int)((t - 0.5f) * 14f);
            if (facing is Facing.Left or Facing.Right)
            {
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 16, tx - 1, ty - 8 + (spread / 2), sortY, feetX, 9200, tint: goldSlash);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 12, tx + (facing == Facing.Right ? 2 : -2), ty - 6, sortY, feetX, 9201, tint: emberSlash);
            }
            else
            {
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 16, 3, tx - 8 + (spread / 2), ty - 1, sortY, feetX, 9200, tint: goldSlash);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 12, 2, tx - 6, ty + (facing == Facing.Down ? 2 : -2), sortY, feetX, 9201, tint: emberSlash);
            }

            if (tool == ToolKind.Scythe)
            {
                int cutBurst = 2 + (int)(t * 8f);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 3, tx - cutBurst, ty - cutBurst, sortY, feetX, 9202, tint: leafCut);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 3, tx + cutBurst, ty - (cutBurst / 2), sortY, feetX, 9203, tint: leafCut);
            }

            return;
        }

        int burst = 2 + (int)(t * 9f);
        switch (tool)
        {
            case ToolKind.WateringCan:
            {
                int waterTint = unchecked((int)0x29b6f6f0);
                int foamTint = unchecked((int)0xb3e5fcf0);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx - burst, ty - 2, sortY, feetX, 9210, tint: waterTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx + burst - 2, ty - 2, sortY, feetX, 9211, tint: waterTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 3, tx, ty - (burst / 2), sortY, feetX, 9212, tint: waterTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - (burst / 2), ty - burst, sortY, feetX, 9213, tint: foamTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx + (burst / 2), ty + (burst / 2), sortY, feetX, 9214, tint: foamTint);
                break;
            }

            case ToolKind.Hoe:
            {
                int soilTint = unchecked((int)0x8d5524f0);
                int pebbleTint = unchecked((int)0xc68b59f0);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx - burst, ty - (burst / 2), sortY, feetX, 9220, tint: soilTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 3, tx + burst - 2, ty - (burst / 2), sortY, feetX, 9221, tint: soilTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx, ty - burst, sortY, feetX, 9222, tint: pebbleTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - (burst / 2), ty + (burst / 2), sortY, feetX, 9223, tint: pebbleTint);
                break;
            }

            case ToolKind.Axe:
            {
                int chipTint = unchecked((int)0xd4a359f0);
                int barkTint = unchecked((int)0x8d5524f0);
                int leafTint = unchecked((int)0x66bb6af0);
                int flashTint = unchecked((int)0xffffffe0);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 8, 2, tx - 4, ty - 1, sortY, feetX, 9230, tint: flashTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, tx - burst, ty - burst, sortY, feetX, 9231, tint: chipTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, tx + burst - 2, ty - (burst / 2), sortY, feetX, 9232, tint: chipTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx + (burst / 2), ty - burst, sortY, feetX, 9233, tint: barkTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, tx - (burst / 2), ty - burst - 4, sortY, feetX, 9234, tint: leafTint);
                break;
            }

            case ToolKind.Pickaxe:
            {
                int sparkTint = unchecked((int)0xffd54ff0);
                int emberTint = unchecked((int)0xff7043f0);
                int shardTint = unchecked((int)0xb0bec5f0);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 4, 4, tx - 2, ty - 2, sortY, feetX, 9240, tint: unchecked((int)0xfffffff0));
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - burst, ty - burst, sortY, feetX, 9241, tint: sparkTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx + burst, ty - burst, sortY, feetX, 9242, tint: sparkTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 2, 2, tx - (burst / 2), ty + (burst / 2), sortY, feetX, 9243, tint: emberTint);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 3, 2, tx + (burst / 2), ty + (burst / 2), sortY, feetX, 9244, tint: shardTint);
                break;
            }

            default:
            {
                int swipeTint = unchecked((int)0xfff3b0c8);
                batch.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 2, tx - 3, ty - 1, sortY, feetX, 9250, tint: swipeTint);
                break;
            }
        }
    }
}
