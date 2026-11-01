// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.ViewModels.Labels;

namespace Nosum.Wasm.Game;

internal sealed partial class MonstersDecorator
{
    // Labels already placed by DrawHazardsAndShrines this frame (text id, anchor x and y); a fixed array, so the frame allocates nothing.
    private readonly (int LabelId, int X, int Y)[] _placedHazardLabels = new (int, int, int)[64];
    private int _placedHazardLabelCount;

    private void DrawRiftPortal(WorldSpriteBatch world, long now)
    {
        if (!string.Equals(_mapId, GameMaps.BusStop, StringComparison.Ordinal))
        {
            return;
        }

        const int portalTileX = 22;
        const int portalTileY = 14;
        int pWorldX = portalTileX * FrameSize;
        int pWorldY = portalTileY * FrameSize;
        if (!this.IsVisible(pWorldX, pWorldY, 32, 32))
        {
            return;
        }

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
            tint: Rgba.From(0x6b, 0x21, 0xa8, 0xcc));

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
            tint: Rgba.From(0x06, 0xb6, 0xd4, 0xee));

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
            world.AddOverlay(
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

    private void DrawHazardsAndShrines(WorldSpriteBatch world, long now)
    {
        _combat.CopyHazards(_hazardsScratch);
        _placedHazardLabelCount = 0;
        for (int i = 0; i < _hazardsScratch.Count; i++)
        {
            HazardClientRow h = _hazardsScratch[i];
            int hWorldX = h.X * FrameSize;
            int hWorldY = h.Y * FrameSize;
            if (!this.IsVisible(hWorldX, hWorldY, 24, 24))
            {
                continue;
            }

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
                    tint: Rgba.From(0x14, 0x53, 0x2d, 0xcc));

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
                    tint: Rgba.From(0x86, 0xef, 0xac, 0xee));
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
                    tint: Rgba.From(0x47, 0x55, 0x69, 0xcc));

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
                        tint: Rgba.From(0xf1, 0xf5, 0xf9, 0xee));
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
                    tint: Rgba.From(0x99, 0x1b, 0x1b, 0xee));

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
                    tint: Rgba.From(0xf5, 0x9e, 0x0b, 0xff));
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
                (int iconSrcX, int iconSrcY, int iconTint) = h.Kind switch
                {
                    4 => (80, 64, RenderCommandBuffer.NoTint),          // Ruby vitality diamond relic
                    5 => (96, 64, RenderCommandBuffer.NoTint),          // Sapphire arcane mana crystal
                    7 => (96, 64, unchecked((int)0xfde047ff)),          // Thunder crystal cluster relic
                    8 => (304, 64, RenderCommandBuffer.NoTint),         // Solar fire core relic
                    9 => (80, 64, unchecked((int)0x7dd3fcff)),          // Glacial frost diamond relic
                    _ => (272, 64, RenderCommandBuffer.NoTint),         // Golden sanctuary crown relic
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
                    8720,
                    tint: iconTint);
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
                if (hLabelId > 0 && this.TakeHazardLabelSlot(hLabelId, hWorldX + 8, hWorldY - 4))
                {
                    world.AddOverlay(
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
    }

    // Places a hazard label unless one of the same text already sits on it this frame (HazardLabelRules, docs/adr/ADR-091). Labels past the
    // array's end are placed as before: the rule only thins clusters, it never drops a label that had no neighbour.
    private bool TakeHazardLabelSlot(int labelId, int x, int y)
    {
        for (int i = 0; i < _placedHazardLabelCount; i++)
        {
            (int placedId, int placedX, int placedY) = _placedHazardLabels[i];
            if (HazardLabelRules.Clashes(labelId, x, y, placedId, placedX, placedY))
            {
                return false;
            }
        }

        if (_placedHazardLabelCount < _placedHazardLabels.Length)
        {
            _placedHazardLabels[_placedHazardLabelCount++] = (labelId, x, y);
        }

        return true;
    }
}
