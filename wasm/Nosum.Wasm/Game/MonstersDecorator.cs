// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Combat;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws live monsters on the active map or mine floor as depth-sorted world sprites from <c>assets/sprites/monsters.png</c>
/// (docs/adr/ADR-045 D3). Allocation-free in the frame loop.
/// </summary>
internal sealed class MonstersDecorator : IMapDecorator
{
    internal const string SheetUrl = "assets/sprites/monsters.png";
    internal const string ItemsSheetUrl = "assets/sprites/items.png";
    private const int FrameSize = 16;
    private const int BlockHeight = 48;

    private readonly string _mapId;
    private readonly CombatChannel _combat;
    private readonly CombatLabels? _labels;
    private readonly int _sheetTexture;
    private readonly int _itemsTexture;
    private readonly List<MonsterClientRow> _scratch = new(16);
    private readonly List<ProjectileClientRow> _projectilesScratch = new(16);
    private readonly List<DeathEffectClientRow> _deathEffectsScratch = new(16);
    private readonly List<FloatingTextClientRow> _floatingTextsScratch = new(32);
    private readonly List<HazardClientRow> _hazardsScratch = new(8);

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">Map id being decorated.</param>
    /// <param name="combat">The client combat channel.</param>
    /// <param name="sheetTexture">Texture id of <c>assets/sprites/monsters.png</c>.</param>
    /// <param name="itemsTexture">Texture id of <c>assets/sprites/items.png</c>.</param>
    /// <param name="labels">Pre-interned floating combat labels; <see langword="null"/> to skip floating combat text.</param>
    public MonstersDecorator(string mapId, CombatChannel combat, int sheetTexture, int itemsTexture, CombatLabels? labels = null)
    {
        _mapId = mapId;
        _combat = combat;
        _sheetTexture = sheetTexture;
        _itemsTexture = itemsTexture;
        _labels = labels;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        if (!string.Equals(_combat.MapId, _mapId, StringComparison.Ordinal))
        {
            return;
        }

        _combat.CopyMonsters(_scratch);
        long now = Environment.TickCount64;

        // Ancient Dungeon Rift Portal on Crossroads map (docs/adr/ADR-057):
        if (string.Equals(_mapId, GameMaps.BusStop, StringComparison.Ordinal))
        {
            const int portalTileX = 22;
            const int portalTileY = 14;
            int pWorldX = portalTileX * FrameSize;
            int pWorldY = portalTileY * FrameSize;

            // Outer dark rift aura (16x16)
            world.Add(
                RenderCommandBuffer.FillTexture,
                0,
                0,
                16,
                16,
                pWorldX,
                pWorldY,
                pWorldY + FrameSize,
                pWorldX,
                8800,
                tint: unchecked((int)0x6b21a8cc));

            // Inner swirling core (10x10)
            float corePhase = now * 0.006f;
            int pulseSize = 8 + (int)(MathF.Sin(corePhase) * 2f);
            int coreOffset = (16 - pulseSize) / 2;
            world.Add(
                RenderCommandBuffer.FillTexture,
                0,
                0,
                pulseSize,
                pulseSize,
                pWorldX + coreOffset,
                pWorldY + coreOffset,
                pWorldY + FrameSize,
                pWorldX,
                8801,
                tint: unchecked((int)0x06b6d4ee));

            // Swirling orbiting particles
            float r = 9f + (MathF.Sin(corePhase * 1.5f) * 3f);
            int ox1 = (int)(pWorldX + 8 + (MathF.Cos(corePhase) * r));
            int oy1 = (int)(pWorldY + 8 + (MathF.Sin(corePhase) * r));
            int ox2 = (int)(pWorldX + 8 - (MathF.Cos(corePhase) * r));
            int oy2 = (int)(pWorldY + 8 - (MathF.Sin(corePhase) * r));
            world.Add(_itemsTexture, 96, 64, 8, 8, ox1 - 4, oy1 - 4, pWorldY + FrameSize, pWorldX, 8802);
            world.Add(_itemsTexture, 96, 64, 8, 8, ox2 - 4, oy2 - 4, pWorldY + FrameSize, pWorldX, 8803);

            // Overhead portal title label
            if (_labels is not null)
            {
                world.Add(
                    RenderCommandBuffer.LabelTexture,
                    _labels.PortalLabelId,
                    0,
                    0,
                    0,
                    pWorldX + 8,
                    pWorldY - 6,
                    pWorldY + FrameSize + 20,
                    pWorldX,
                    8804);
            }
        }

        // Environmental Hazards (docs/adr/ADR-061):
        _combat.CopyHazards(_hazardsScratch);
        for (int i = 0; i < _hazardsScratch.Count; i++)
        {
            HazardClientRow h = _hazardsScratch[i];
            int hWorldX = h.X * FrameSize;
            int hWorldY = h.Y * FrameSize;

            if (h.Kind == 1) // SporeVent (poison gas vent)
            {
                // Dark mossy vent base (14x14)
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    14,
                    14,
                    hWorldX + 1,
                    hWorldY + 1,
                    hWorldY + 1,
                    hWorldX + 1,
                    8400,
                    tint: unchecked((int)0x14532dcc));

                // Pulsing toxic spore vapor (0xRRGGBBAA)
                float pulse = MathF.Sin((now * 0.005f) + (h.HazardId * 1.5f));
                int alpha = Math.Clamp(160 + (int)(pulse * 45f), 100, 220);
                int vaporTint = (0x22c55e << 8) | alpha;
                int vaporSize = 12 + (int)(pulse * 2f);
                int vaporOffset = (16 - vaporSize) / 2;
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    vaporSize,
                    vaporSize,
                    hWorldX + vaporOffset,
                    hWorldY + vaporOffset,
                    hWorldY + FrameSize,
                    hWorldX,
                    8700,
                    tint: vaporTint);

                // Bright toxic spore core
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    6,
                    6,
                    hWorldX + 5,
                    hWorldY + 5,
                    hWorldY + FrameSize,
                    hWorldX,
                    8701,
                    tint: unchecked((int)0x86efacee));
            }
            else if (h.Kind == 2) // SpikeTrap (floor spikes)
            {
                // Steel plate base
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    14,
                    14,
                    hWorldX + 1,
                    hWorldY + 1,
                    hWorldY + 1,
                    hWorldX + 1,
                    8400,
                    tint: unchecked((int)0x475569cc));

                // Periodic spike thrust cycle (2.4s period)
                float spikeCycle = ((now + (h.HazardId * 700)) % 2400) / 2400f;
                if (spikeCycle > 0.65f)
                {
                    world.Add(
                        RenderCommandBuffer.FillTexture,
                        0,
                        0,
                        10,
                        10,
                        hWorldX + 3,
                        hWorldY + 3,
                        hWorldY + FrameSize,
                        hWorldX,
                        8750,
                        tint: unchecked((int)0xf1f5f9ee));
                }
            }
            else if (h.Kind == 3) // MagmaFissure (molten crack)
            {
                // Fiery molten fissure
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    14,
                    14,
                    hWorldX + 1,
                    hWorldY + 1,
                    hWorldY + 1,
                    hWorldX + 1,
                    8400,
                    tint: unchecked((int)0x991b1bee));

                // Boiling eruptive flame core
                float magmaPulse = MathF.Sin((now * 0.008f) + (h.HazardId * 2f));
                int flameSize = 8 + (int)(magmaPulse * 3f);
                int flameOffset = (16 - flameSize) / 2;
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    flameSize,
                    flameSize,
                    hWorldX + flameOffset,
                    hWorldY + flameOffset,
                    hWorldY + FrameSize,
                    hWorldX,
                    8700,
                    tint: unchecked((int)0xf59e0bff));
            }
            else if (h.Kind is >= 4 and <= 9) // Dungeon Blessing Shrines & Elemental Resonance Pylons (docs/adr/ADR-063, ADR-064)
            {
                // Carved stone / elemental sanctuary pedestal base (14x14)
                int baseTint = h.Kind switch
                {
                    4 => unchecked((int)0x064e3bee), // Deep emerald stone
                    5 => unchecked((int)0x0f172aee), // Obsidian arcane stone
                    7 => unchecked((int)0x312e81ee), // Indigo storm monolith stone
                    8 => unchecked((int)0x7c2d12ee), // Amber solar sandstone
                    9 => unchecked((int)0x0c4a6eee), // Glacial frost granite
                    _ => unchecked((int)0x78350fee), // Royal sanctuary bronze-gold
                };
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    14,
                    14,
                    hWorldX + 1,
                    hWorldY + 1,
                    hWorldY + 1,
                    hWorldX + 1,
                    8410,
                    tint: baseTint);

                // Expanding elemental resonance shockwave ring for pylons (kinds 7..9)
                if (h.Kind >= 7)
                {
                    float wavePhase = ((now + (h.HazardId * 310)) % 1800) / 1800f;
                    int ringSize = 14 + (int)(wavePhase * 12f);
                    int ringOffset = (16 - ringSize) / 2;
                    int ringRgb = h.Kind switch
                    {
                        7 => 0xa855f7, // Violet-gold thunder resonance
                        8 => 0xf97316, // Solar radiance wave
                        _ => 0x38bdf8, // Glacial frost wave
                    };
                    int ringAlpha = Math.Clamp((int)((1f - wavePhase) * 140f), 25, 140);
                    world.Add(
                        RenderCommandBuffer.FillTexture,
                        0,
                        0,
                        ringSize,
                        ringSize,
                        hWorldX + ringOffset,
                        hWorldY + ringOffset,
                        hWorldY + 2,
                        hWorldX,
                        8420,
                        tint: (ringRgb << 8) | ringAlpha);
                }

                // Pulsing sanctuary blessing aura ring
                float shrinePulse = MathF.Sin((now * 0.005f) + h.HazardId);
                int auraSize = 12 + (int)(shrinePulse * 2f);
                int auraOffset = (16 - auraSize) / 2;
                int auraRgb = h.Kind switch
                {
                    4 => 0x10b981, // Emerald vitality glow
                    5 => 0x06b6d4, // Cyan arcane mana glow
                    7 => 0xeab308, // Electric lightning gold glow
                    8 => 0xf59e0b, // Solar amber glow
                    9 => 0x38bdf8, // Glacial ice blue glow
                    _ => 0xfbbf24, // Radiant gold sanctuary glow
                };
                int auraAlpha = Math.Clamp(185 + (int)(shrinePulse * 45f), 130, 235);
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    auraSize,
                    auraSize,
                    hWorldX + auraOffset,
                    hWorldY + auraOffset,
                    hWorldY + FrameSize,
                    hWorldX,
                    8710,
                    tint: (auraRgb << 8) | auraAlpha);

                // Levitating crystal / star relic from items.png bobbing above the altar
                int bobY = (int)(MathF.Sin((now * 0.006f) + h.HazardId) * 2f);
                (int iconSrcX, int iconSrcY) = h.Kind switch
                {
                    4 => (80, 64),  // Ruby vitality core relic
                    5 => (96, 64),  // Emerald arcane mana crystal
                    7 => (336, 64), // Thunder crystal cluster relic
                    8 => (320, 64), // Solar fire crystal cluster relic
                    9 => (64, 48),  // Glacial frost crystal relic
                    _ => (112, 64), // Golden sanctuary relic
                };
                world.Add(
                    _itemsTexture,
                    iconSrcX,
                    iconSrcY,
                    16,
                    16,
                    hWorldX,
                    hWorldY - 4 + bobY,
                    hWorldY + FrameSize,
                    hWorldX,
                    8720);
            }

            if (_labels is not null)
            {
                int hLabelId = h.Kind switch
                {
                    1 => _labels.HazardSporeLabelId,
                    2 => _labels.HazardSpikeLabelId,
                    3 => _labels.HazardMagmaLabelId,
                    4 => _labels.ShrineVitalityLabelId,
                    5 => _labels.ShrineArcaneLabelId,
                    6 => _labels.ShrineEmpowermentLabelId,
                    7 => _labels.ShrineThunderLabelId,
                    8 => _labels.ShrineSolarLabelId,
                    9 => _labels.ShrineFrostLabelId,
                    _ => 0,
                };
                if (hLabelId > 0)
                {
                    world.Add(
                        RenderCommandBuffer.LabelTexture,
                        hLabelId,
                        0,
                        0,
                        0,
                        hWorldX + 8,
                        hWorldY - 4,
                        hWorldY + FrameSize + 10,
                        hWorldX,
                        8790);
                }
            }
        }

        for (int i = 0; i < _scratch.Count; i++)
        {
            MonsterClientRow m = _scratch[i];
            int kindIndex = Math.Clamp(m.Kind - 1, 0, 11);
            int frameCol = (int)(((now / 220) + m.MonsterId) & 3);
            float phase = (now + (m.MonsterId * 313)) * 0.0035f;
            int swayX = (int)MathF.Round(MathF.Sin(phase) * 3f);
            int dirRow = swayX > 1 ? 1 : 0;

            int srcX = frameCol * FrameSize;
            int srcY = (kindIndex * BlockHeight) + (dirRow * FrameSize);
            int worldX = (m.X * FrameSize) + swayX;
            int worldY = m.Y * FrameSize;

            // Elite monster radiant aura (docs/adr/ADR-058)
            if (m.Modifier > 0)
            {
                int auraTint = m.Modifier switch
                {
                    1 => unchecked((int)0xff450077), // Flame Aura (fiery orange-red)
                    2 => unchecked((int)0x06b6d477), // Swift (cyan electric)
                    3 => unchecked((int)0xfacc1588), // Shielded (gold diamond)
                    4 => unchecked((int)0xb91c1c88), // Vampiric (crimson)
                    _ => unchecked((int)0xffffff55),
                };

                int pulse = (int)(((now / 150) + m.MonsterId) & 3);
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    FrameSize + 4 + pulse,
                    FrameSize + 4 + pulse,
                    worldX - 2 - (pulse / 2),
                    worldY - 4 - (pulse / 2),
                    worldY + FrameSize - 1,
                    worldX,
                    8700 + m.MonsterId,
                    tint: auraTint);
            }

            world.Add(
                _sheetTexture,
                srcX,
                srcY,
                FrameSize,
                FrameSize,
                worldX,
                worldY - 2,
                worldY + FrameSize,
                worldX + (FrameSize / 2),
                m.MonsterId);

            // Elite title badge (docs/adr/ADR-058)
            if (m.Modifier > 0 && _labels is not null)
            {
                int eliteLabelId = m.Modifier switch
                {
                    1 => _labels.EliteFlameLabelId,
                    2 => _labels.EliteSwiftLabelId,
                    3 => _labels.EliteShieldedLabelId,
                    4 => _labels.EliteVampiricLabelId,
                    _ => 0,
                };

                if (eliteLabelId > 0)
                {
                    world.Add(
                        RenderCommandBuffer.LabelTexture,
                        0,
                        0,
                        eliteLabelId,
                        0,
                        worldX - 10,
                        worldY - 14,
                        worldY + FrameSize + 15,
                        worldX,
                        8750 + m.MonsterId);
                }
            }

            // Overhead mini-health bar (docs/adr/ADR-055, ADR-058):
            // Displayed if the monster is damaged (Hp < MaxHp), a floor boss (Kind >= 9), or an elite monster.
            if (m.MaxHp > 0 && (m.Hp < m.MaxHp || m.Kind >= 9 || m.Modifier > 0))
            {
                int barW = 14;
                int barH = 3;
                int barX = worldX + 1;
                int barY = worldY - 6;

                // Background & border (dark slate):
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    barW,
                    barH,
                    barX,
                    barY,
                    worldY + FrameSize,
                    worldX,
                    8900 + m.MonsterId,
                    tint: unchecked((int)0x111111ee));

                // Health fill:
                int fillW = Math.Clamp((m.Hp * (barW - 2)) / m.MaxHp, 1, barW - 2);
                int hpTint = (m.Hp * 100 / m.MaxHp) switch
                {
                    > 50 => unchecked((int)0x44cc44ff), // Green
                    > 25 => unchecked((int)0xe5b82eff), // Yellow
                    _ => unchecked((int)0xe53935ff),    // Red
                };

                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    fillW,
                    barH - 2,
                    barX + 1,
                    barY + 1,
                    worldY + FrameSize,
                    worldX,
                    8901 + m.MonsterId,
                    tint: hpTint);
            }
        }

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

                if (p.Kind == 2)
                {
                    int flameOuter = unchecked((int)0xe64a19e8);
                    int flameMid = unchecked((int)0xff9800f4);
                    int flameCore = unchecked((int)0xfff59dff);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 12, 12, pWorldX + 2, pWorldY + 2, pWorldY + FrameSize, pWorldX + 8, 8997, tint: flameOuter);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 8, 8, pWorldX + 4, pWorldY + 4, pWorldY + FrameSize + 1, pWorldX + 8, 8998, tint: flameMid);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 4, 4, pWorldX + 6, pWorldY + 6, pWorldY + FrameSize + 2, pWorldX + 8, 8999, tint: flameCore);

                    float tailProgress = Math.Max(0f, progress - 0.14f);
                    int tailX = (int)((p.StartX + ((p.TargetX - p.StartX) * tailProgress)) * FrameSize);
                    int tailY = (int)((p.StartY + ((p.TargetY - p.StartY) * tailProgress)) * FrameSize);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 5, 5, tailX + 5, tailY + 5, tailY + FrameSize, tailX + 8, 9000, tint: flameOuter);
                }
                else
                {
                    world.Add(
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

                world.Add(_itemsTexture, 96, 64, 8, 8, twX + off, twY + off, twY + FrameSize, twX, 9001);
                world.Add(_itemsTexture, 96, 64, 8, 8, twX - off, twY - off, twY + FrameSize, twX, 9002);
                world.Add(_itemsTexture, 96, 64, 8, 8, twX - off, twY + off, twY + FrameSize, twX, 9003);
                world.Add(_itemsTexture, 96, 64, 8, 8, twX + off, twY - off, twY + FrameSize, twX, 9004);
                if (p.Kind == 2)
                {
                    int burstTint = unchecked((int)0xff7043e0);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX + off + 4, twY + 4, twY + FrameSize + 1, twX, 9005, tint: burstTint);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX - off + 4, twY + 4, twY + FrameSize + 1, twX, 9006, tint: burstTint);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX + 4, twY + off + 4, twY + FrameSize + 1, twX, 9007, tint: burstTint);
                    world.Add(RenderCommandBuffer.FillTexture, 0, 0, 6, 6, twX + 4, twY - off + 4, twY + FrameSize + 1, twX, 9008, tint: burstTint);
                }
            }
        }

        _combat.CopyDeathEffects(_deathEffectsScratch, now);
        for (int i = 0; i < _deathEffectsScratch.Count; i++)
        {
            DeathEffectClientRow d = _deathEffectsScratch[i];
            float elapsed = now - d.StartTime;
            int frameCol = Math.Clamp((int)(elapsed / (d.DurationMs / 4f)), 0, 3);
            int srcX = frameCol * FrameSize;
            int srcY = (12 * BlockHeight) + (d.Kind * FrameSize);
            int worldX = d.X * FrameSize;
            int worldY = d.Y * FrameSize;

            world.Add(
                _sheetTexture,
                srcX,
                srcY,
                FrameSize,
                FrameSize,
                worldX,
                worldY - 2,
                worldY + FrameSize,
                worldX + (FrameSize / 2),
                9500 + i);
        }

        if (_labels is not null)
        {
            _combat.CopyFloatingTexts(_floatingTextsScratch, now);
            for (int i = 0; i < _floatingTextsScratch.Count; i++)
            {
                FloatingTextClientRow f = _floatingTextsScratch[i];
                float progress = Math.Clamp((now - f.StartTime) / f.DurationMs, 0f, 1f);
                int floatY = (int)(f.WorldY - (progress * 18f));
                int labelId = _labels.GetLabelId(f.Amount, f.Style);

                world.Add(
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

        // Celebratory Level-Up Radiance Beacon & Overhead Title Banner (docs/adr/ADR-062)
        if (_combat.TryGetActiveLevelUp(now, out float lvlProgress, out _, out int pTileX, out int pTileY))
        {
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
            world.Add(
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
            world.Add(
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
                world.Add(
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
                world.Add(
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
    }
}

/// <summary>Builds <see cref="MonstersDecorator"/> instances for maps.</summary>
internal sealed class MonstersDecoratorProvider : IMapDecoratorProvider
{
    private readonly CombatChannel _combat;
    private readonly CombatLabels? _labels;

    /// <summary>Initializes the provider.</summary>
    /// <param name="combat">The combat channel.</param>
    /// <param name="labels">Pre-interned floating combat labels.</param>
    public MonstersDecoratorProvider(CombatChannel combat, CombatLabels? labels = null)
    {
        _combat = combat;
        _labels = labels;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        int sheet = await textures.LoadAsync(MonstersDecorator.SheetUrl).ConfigureAwait(false);
        int itemsSheet = await textures.LoadAsync(MonstersDecorator.ItemsSheetUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _combat.LoadMonsters();
        return new MonstersDecorator(spec.Id, _combat, sheet, itemsSheet, _labels);
    }
}
