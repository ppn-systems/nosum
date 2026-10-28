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

            // Overhead mini-health bar (docs/adr/ADR-055):
            // Displayed if the monster is damaged (Hp < MaxHp) or is a floor boss (Kind >= 9).
            if (m.MaxHp > 0 && (m.Hp < m.MaxHp || m.Kind >= 9))
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
            else
            {
                float impact = (now - (p.StartTime + (long)p.DurationMs)) / 200f;
                int off = (int)(impact * 8f);
                int twX = (int)(p.TargetX * FrameSize);
                int twY = (int)(p.TargetY * FrameSize);

                world.Add(_itemsTexture, 96, 64, 8, 8, twX + off, twY + off, twY + FrameSize, twX, 9001);
                world.Add(_itemsTexture, 96, 64, 8, 8, twX - off, twY - off, twY + FrameSize, twX, 9002);
                world.Add(_itemsTexture, 96, 64, 8, 8, twX - off, twY + off, twY + FrameSize, twX, 9003);
                world.Add(_itemsTexture, 96, 64, 8, 8, twX + off, twY - off, twY + FrameSize, twX, 9004);
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
