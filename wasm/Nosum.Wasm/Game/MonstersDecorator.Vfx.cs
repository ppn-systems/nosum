// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Combat;

namespace Nosum.Wasm.Game;

internal sealed partial class MonstersDecorator
{
    private void DrawProjectiles(WorldSpriteBatch world, long now)
    {
        _combat.CopyProjectiles(_projectilesScratch, now);
        for (int i = 0; i < _projectilesScratch.Count; i++)
        {
            ProjectileClientRow p = _projectilesScratch[i];
            float progress = Math.Clamp((now - p.StartTime) / p.DurationMs, 0f, 1f);
            if (progress < 1.0f)
            {
                float px = p.StartX + ((p.TargetX - p.StartX) * progress);
                float py = p.StartY + ((p.TargetY - p.StartY) * progress);
                int pWorldX = (int)(px * FrameSize);
                int pWorldY = (int)(py * FrameSize);
                if (!this.IsVisible(pWorldX, pWorldY))
                {
                    continue;
                }

                if (p.Kind == 2)
                {
                    int flameOuter = unchecked((int)0xe64a19e8);
                    int flameMid = unchecked((int)0xff9800f4);
                    int flameCore = unchecked((int)0xfff59dff);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 12, 12, pWorldX + 2, pWorldY + 2, pWorldY + FrameSize, pWorldX + 8, 8997, tint: flameOuter);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 8, 8, pWorldX + 4, pWorldY + 4, pWorldY + FrameSize + 1, pWorldX + 8, 8998, tint: flameMid);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 4, 4, pWorldX + 6, pWorldY + 6, pWorldY + FrameSize + 2, pWorldX + 8, 8999, tint: flameCore);

                    float tailProgress = Math.Max(0f, progress - 0.14f);
                    int tailX = (int)((p.StartX + ((p.TargetX - p.StartX) * tailProgress)) * FrameSize);
                    int tailY = (int)((p.StartY + ((p.TargetY - p.StartY) * tailProgress)) * FrameSize);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 5, 5, tailX + 5, tailY + 5, tailY + FrameSize, tailX + 8, 9000, tint: flameOuter);
                }
                else
                {
                    world.AddOverlay(
                        _itemsTexture,
                        96,
                        64,
                        FrameSize,
                        FrameSize,
                        pWorldX,
                        pWorldY,
                        pWorldY + FrameSize,
                        pWorldX + (FrameSize / 2),
                        9000);
                }
            }
            else
            {
                float impact = (now - (p.StartTime + (long)p.DurationMs)) / 200f;
                int off = (int)(impact * (p.Kind == 2 ? 12f : 8f));
                int twX = (int)(p.TargetX * FrameSize);
                int twY = (int)(p.TargetY * FrameSize);
                if (!this.IsVisible(twX, twY, 24, 24))
                {
                    continue;
                }

                int shrapnelSrcX = p.Kind == 2 ? 64 : 96;
                world.AddOverlay(_itemsTexture, shrapnelSrcX, 64, 8, 8, twX + off, twY + off, twY + FrameSize, twX, 9001);
                world.AddOverlay(_itemsTexture, shrapnelSrcX, 64, 8, 8, twX - off, twY - off, twY + FrameSize, twX, 9002);
                world.AddOverlay(_itemsTexture, shrapnelSrcX, 64, 8, 8, twX - off, twY + off, twY + FrameSize, twX, 9003);
                world.AddOverlay(_itemsTexture, shrapnelSrcX, 64, 8, 8, twX + off, twY - off, twY + FrameSize, twX, 9004);
                if (p.Kind == 2)
                {
                    int burstTint = unchecked((int)0xff7043e0);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX + off + 4, twY + 4, twY + FrameSize + 1, twX, 9005, tint: burstTint);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX - off + 4, twY + 4, twY + FrameSize + 1, twX, 9006, tint: burstTint);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX + 4, twY + off + 4, twY + FrameSize + 1, twX, 9007, tint: burstTint);
                    world.AddOverlay(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX + 4, twY - off + 4, twY + FrameSize + 1, twX, 9008, tint: burstTint);
                }
            }
        }
    }

    private void DrawDeathEffects(WorldSpriteBatch world, long now)
    {
        _combat.CopyDeathEffects(_deathEffectsScratch, now);
        for (int i = 0; i < _deathEffectsScratch.Count; i++)
        {
            DeathEffectClientRow d = _deathEffectsScratch[i];
            int worldX = d.X * FrameSize;
            int worldY = d.Y * FrameSize;
            if (!this.IsVisible(worldX, worldY))
            {
                continue;
            }

            float elapsed = now - d.StartTime;
            int frameCol = Math.Clamp((int)(elapsed / (d.DurationMs / 3f)), 0, 2);
            int srcX = frameCol * FrameSize;
            int srcY = (12 * BlockHeight) + (d.Kind * FrameSize);
            int alpha = Math.Clamp(255 - (int)(elapsed * 200f / Math.Max(1f, d.DurationMs)), 40, 255);
            int deathTint = unchecked((int)0xffffff00) | alpha;

            world.AddOverlay(
                _sheetTexture,
                srcX,
                srcY,
                FrameSize,
                FrameSize,
                worldX,
                worldY - 2,
                worldY + FrameSize,
                worldX + (FrameSize / 2),
                9500 + i,
                tint: deathTint);
        }
    }

    private void DrawFloatingTexts(WorldSpriteBatch world, long now)
    {
        if (_labels is null)
        {
            return;
        }

        _combat.CopyFloatingTexts(_floatingTextsScratch, now);
        for (int i = 0; i < _floatingTextsScratch.Count; i++)
        {
            FloatingTextClientRow f = _floatingTextsScratch[i];
            if (!this.IsVisible(f.WorldX, f.WorldY, 32, 32))
            {
                continue;
            }

            float progress = Math.Clamp((now - f.StartTime) / f.DurationMs, 0f, 1f);
            int floatY = (int)(f.WorldY - (progress * 18f));
            int labelId = _labels.GetLabelId(f.Amount, f.Style);

            world.AddOverlay(
                RenderCommandBuffer.LabelTexture,
                labelId,
                0,
                0,
                0,
                f.WorldX,
                floatY,
                f.WorldY + FrameSize + 30,
                f.WorldX,
                9800 + i);
        }
    }

    private void DrawLevelUpBeacon(WorldSpriteBatch world, long now)
    {
        // Celebratory Level-Up Radiance Beacon & Overhead Title Banner (docs/adr/ADR-062)
        if (!_combat.TryGetActiveLevelUp(now, out float lvlProgress, out _, out int pTileX, out int pTileY))
        {
            return;
        }

        int pWorldX = pTileX * FrameSize;
        int pWorldY = pTileY * FrameSize;
        float fade = lvlProgress < 0.8f ? 1f : (1f - lvlProgress) * 5f;
        int outerAlpha = Math.Clamp((int)(fade * 120f), 20, 140);
        int innerAlpha = Math.Clamp((int)(fade * 200f), 40, 220);

        // Expanding golden ground halo
        int haloSize = 24 + (int)(MathF.Sin(now * 0.012f) * 4f);
        int haloOff = (FrameSize - haloSize) / 2;
        world.Add(
            RenderCommandBuffer.FillTexture,
            0,
            0,
            haloSize,
            haloSize,
            pWorldX + haloOff,
            pWorldY + haloOff + 2,
            pWorldY + FrameSize - 1,
            pWorldX,
            8850,
            tint: unchecked((int)((0xfacc15 << 8) | outerAlpha)));

        // Outer celestial golden pillar of light
        world.AddOverlay(
            RenderCommandBuffer.FillTexture,
            0,
            0,
            14,
            48,
            pWorldX + 1,
            pWorldY - 34,
            pWorldY + FrameSize + 20,
            pWorldX,
            9750,
            tint: unchecked((int)((0xfde047 << 8) | outerAlpha)));

        // Inner radiant core beam
        world.AddOverlay(
            RenderCommandBuffer.FillTexture,
            0,
            0,
            6,
            52,
            pWorldX + 5,
            pWorldY - 38,
            pWorldY + FrameSize + 21,
            pWorldX,
            9751,
            tint: unchecked((int)((0xfef9c3 << 8) | innerAlpha)));

        // 4 ascending starlight sparkles spiraling up the beacon
        for (int s = 0; s < 4; s++)
        {
            float sPhase = ((now * 0.003f) + (s * 0.25f)) % 1f;
            float angle = (now * 0.008f) + (s * 1.57f);
            int sx = pWorldX + 6 + (int)(MathF.Cos(angle) * 10f);
            int sy = pWorldY + 10 - (int)(sPhase * 44f);
            world.AddOverlay(
                RenderCommandBuffer.FillTexture,
                0,
                0,
                4,
                4,
                sx,
                sy,
                pWorldY + FrameSize + 22,
                sx,
                9752 + s,
                tint: unchecked((int)((0xffffff << 8) | innerAlpha)));
        }

        if (_labels is not null)
        {
            int bannerRise = (int)(MathF.Min(1f, lvlProgress * 2.5f) * 14f);
            world.AddOverlay(
                RenderCommandBuffer.LabelTexture,
                _labels.LevelUpLabelId,
                0,
                0,
                0,
                pWorldX + 8,
                pWorldY - 16 - bannerRise,
                pWorldY + FrameSize + 35,
                pWorldX,
                9900);
        }
    }

    private void DrawComboCounter(WorldSpriteBatch world)
    {
        // Floating combo counter above the player while a streak is live (docs/adr/ADR-060).
        int comboLabelId = _labels?.GetComboLabelId(_combat.CurrentCombo) ?? 0;
        if (comboLabelId == 0)
        {
            return;
        }

        int pWorldX = _combat.PlayerTileX * FrameSize;
        int pWorldY = _combat.PlayerTileY * FrameSize;
        world.AddOverlay(
            RenderCommandBuffer.LabelTexture,
            comboLabelId,
            0,
            0,
            0,
            pWorldX + 8,
            pWorldY - 6,
            pWorldY + FrameSize + 35,
            pWorldX,
            9905);
    }

    private void DrawComboFinisher(WorldSpriteBatch world, long now)
    {
        // 5x combo finisher: a gold flash and a ring of sparks on the struck target, with a banner (docs/adr/ADR-060).
        if (!_combat.TryGetActiveFinisher(now, out float progress, out int tileX, out int tileY))
        {
            return;
        }

        int pWorldX = tileX * FrameSize;
        int pWorldY = tileY * FrameSize;
        int cx = pWorldX + (FrameSize / 2);
        int cy = pWorldY + (FrameSize / 2);
        int alpha = Math.Clamp((int)((1f - progress) * 255f), 0, 255);

        // Bright flash that expands and fades over the target
        int flashSize = 10 + (int)(progress * 24f);
        world.AddOverlay(
            RenderCommandBuffer.FillTexture,
            0,
            0,
            flashSize,
            flashSize,
            cx - (flashSize / 2),
            cy - (flashSize / 2),
            pWorldY + FrameSize - 1,
            pWorldX,
            9753,
            tint: unchecked((int)((0xfef9c3 << 8) | alpha)));

        // Eight gold sparks flung outward from the impact
        for (int s = 0; s < 8; s++)
        {
            float angle = s * (MathF.PI / 4f);
            float dist = 4f + (progress * 20f);
            int sx = cx + (int)(MathF.Cos(angle) * dist) - 1;
            int sy = cy + (int)(MathF.Sin(angle) * dist) - 1;
            world.AddOverlay(
                RenderCommandBuffer.FillTexture,
                0,
                0,
                3,
                3,
                sx,
                sy,
                pWorldY + FrameSize + 22,
                sx,
                9760 + s,
                tint: unchecked((int)((0xffe08a << 8) | alpha)));
        }

        if (_labels is not null)
        {
            int bannerRise = (int)(MathF.Min(1f, progress * 2.5f) * 14f);
            world.AddOverlay(
                RenderCommandBuffer.LabelTexture,
                _labels.FinisherLabelId,
                0,
                0,
                0,
                pWorldX + 8,
                pWorldY - 26 - bannerRise,
                pWorldY + FrameSize + 35,
                pWorldX,
                9770);
        }
    }
}
