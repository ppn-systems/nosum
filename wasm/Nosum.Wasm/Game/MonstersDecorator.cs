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
internal sealed partial class MonstersDecorator : IMapDecorator
{
    internal const string SheetUrl = "assets/sprites/monsters.png";
    internal const string ItemsSheetUrl = "assets/sprites/items.png";
    private const int FrameSize = 16;
    private const int BlockHeight = 48;
    private const int CullMargin = 32;

    // Hit feedback durations (docs/adr/ADR-076): the white flash, the hit-stop that holds the sprite's pose, and the slide to a new tile.
    private const int HitFlashMs = 90;
    private const int HitStopMs = 60;
    private const int KnockSlideMs = 100;

    // Strike wind-up (docs/adr/ADR-094): the monster rises a few pixels over the wind-up and flashes white every 80 ms, so the player
    // sees the strike coming and can step out of reach. Reduced motion keeps the flash and drops the rise.
    private const int WindupFlashPeriodMs = 80;
    private const int WindupMaxRisePx = 3;
    private const int WindupFlashAlpha = 160;

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
    private readonly List<StrikeWindupClientRow> _windupScratch = new(16);
    private Camera2D _camera;
    private bool _hasCamera;
    private long _clockMs;

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
    public void SetView(in Camera2D camera)
    {
        _camera = camera;
        _hasCamera = camera.Width > 0 && camera.Height > 0;
    }

    /// <inheritdoc/>
    public void SetClock(in FrameClock clock) => _clockMs = clock.LocalMs;

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        if (!string.Equals(_combat.MapId, _mapId, StringComparison.Ordinal))
        {
            return;
        }

        long now = _clockMs != 0 ? _clockMs : Environment.TickCount64;
        this.DrawRiftPortal(world, now);
        this.DrawHazardsAndShrines(world, now);
        this.DrawGroundTelegraphs(world, now);
        this.DrawMonsters(world, now);
        this.DrawProjectiles(world, now);
        this.DrawDeathEffects(world, now);
        this.DrawFloatingTexts(world, now);
        this.DrawLevelUpBeacon(world, now);
        this.DrawComboCounter(world);
        this.DrawComboFinisher(world, now);
    }

    private bool IsVisible(int worldX, int worldY, int width = FrameSize, int height = FrameSize)
    {
        if (!_hasCamera)
        {
            return true;
        }

        return worldX + width + CullMargin > _camera.Left
            && worldX - CullMargin < _camera.Left + _camera.Width
            && worldY + height + CullMargin > _camera.Top
            && worldY - CullMargin < _camera.Top + _camera.Height;
    }

    private void DrawMonsters(WorldSpriteBatch world, long now)
    {
        _combat.CopyMonsters(_scratch);
        _combat.CopyStrikeWindups(_windupScratch);
        for (int i = 0; i < _scratch.Count; i++)
        {
            MonsterClientRow m = _scratch[i];
            bool reduceMotion = ViewOptions.ReduceMotion;

            // A monster that just moved (a knockback or a step) slides from the tile it left; reduced motion snaps it there.
            int offsetX = 0;
            int offsetY = 0;
            if (!reduceMotion && _combat.TryGetMonsterSlide(m.MonsterId, out long slideTick, out int fromX, out int fromY))
            {
                long sinceSlide = now - slideTick;
                if (sinceSlide >= 0 && sinceSlide < KnockSlideMs)
                {
                    int left = (int)(KnockSlideMs - sinceSlide);
                    offsetX = (fromX - m.X) * FrameSize * left / KnockSlideMs;
                    offsetY = (fromY - m.Y) * FrameSize * left / KnockSlideMs;
                }
            }

            int baseWorldX = (m.X * FrameSize) + offsetX;
            int worldY = (m.Y * FrameSize) + offsetY;
            if (!this.IsVisible(baseWorldX, worldY))
            {
                continue;
            }

            // A landed hit flashes the sprite white (docs/adr/ADR-076). A heavy hit or a finisher flashes brighter; the hit-stop holds the
            // sprite's pose for a moment, and reduced motion keeps it moving.
            int flashAlpha = 0;
            long motionNow = now;
            if (_combat.TryGetMonsterHit(m.MonsterId, out long hitTick, out int impact))
            {
                long sinceHit = now - hitTick;
                if (sinceHit >= 0 && sinceHit < HitFlashMs)
                {
                    int peak = impact == CombatChannel.ImpactLight ? 128 : 192;
                    flashAlpha = ((peak * (HitFlashMs - (int)sinceHit)) / HitFlashMs) & ~63;
                }

                if (!reduceMotion && sinceHit >= 0 && sinceHit < HitStopMs)
                {
                    motionNow = hitTick;
                }
            }

            // A monster in its strike wind-up rises and flashes until the strike lands (docs/adr/ADR-094).
            int windupRise = 0;
            for (int w = 0; w < _windupScratch.Count; w++)
            {
                StrikeWindupClientRow windup = _windupScratch[w];
                long windupElapsed = now - windup.StartMs;
                if (windup.MonsterId != m.MonsterId || windupElapsed < 0 || windupElapsed >= windup.DelayMs)
                {
                    continue;
                }

                if (!reduceMotion)
                {
                    windupRise = 1 + (int)((windupElapsed * WindupMaxRisePx) / windup.DelayMs);
                }

                if (((windupElapsed / WindupFlashPeriodMs) & 1) == 1)
                {
                    flashAlpha = Math.Max(flashAlpha, WindupFlashAlpha);
                }

                break;
            }

            int kindIndex = Math.Clamp(m.Kind - 1, 0, 11);
            int frameCol = (int)(((motionNow / 220) + m.MonsterId) & 3);
            float phase = (motionNow + (m.MonsterId * 313)) * 0.0035f;
            int swayX = (int)MathF.Round(MathF.Sin(phase) * 3f);
            int swayY = (int)MathF.Round(MathF.Cos(phase) * 2f);
            int dirRow = swayX is > 1 or < -1 ? 1 : swayY < -1 ? 2 : 0;
            int monsterFlags = swayX < -1 ? RenderCommandBuffer.FlipX : 0;

            int srcX = frameCol * FrameSize;
            int srcY = (kindIndex * BlockHeight) + (dirRow * FrameSize);
            int worldX = baseWorldX + swayX;

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

                int pulse = (int)(((motionNow / 150) + m.MonsterId) & 3);
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

            // Enraged boss glow: a pulsing red aura at or below the species HP threshold (docs/adr/ADR-047)
            if (m.Enraged)
            {
                int enragePulse = (int)(((motionNow / 120) + m.MonsterId) & 3);
                world.Add(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    FrameSize + 6 + enragePulse,
                    FrameSize + 6 + enragePulse,
                    worldX - 3 - (enragePulse / 2),
                    worldY - 5 - (enragePulse / 2),
                    worldY + FrameSize - 1,
                    worldX,
                    8700 + m.MonsterId,
                    tint: unchecked((int)0xdc262699));
            }

            world.Add(
                _sheetTexture,
                srcX,
                srcY,
                FrameSize,
                FrameSize,
                worldX,
                worldY - 2 - windupRise,
                worldY + FrameSize,
                worldX + (FrameSize / 2),
                m.MonsterId,
                flags: flashAlpha > 0 ? monsterFlags | RenderCommandBuffer.Whiten : monsterFlags,
                tint: flashAlpha > 0 ? unchecked((int)0xffffff00) | flashAlpha : RenderCommandBuffer.NoTint);

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
                    world.AddOverlay(
                        RenderCommandBuffer.LabelTexture,
                        eliteLabelId,
                        0,
                        0,
                        0,
                        worldX + 8,
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
                world.AddOverlay(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    barW,
                    barH,
                    barX,
                    barY,
                    worldY + FrameSize,
                    worldX,
                    8900 + (m.MonsterId * 2),
                    tint: unchecked((int)0x111111ee));

                // Health fill:
                int fillW = Math.Clamp((m.Hp * (barW - 2)) / m.MaxHp, 1, barW - 2);
                int hpTint = (m.Hp * 100 / m.MaxHp) switch
                {
                    > 50 => unchecked((int)0x44cc44ff), // Green
                    > 25 => unchecked((int)0xe5b82eff), // Yellow
                    _ => unchecked((int)0xe53935ff),    // Red
                };

                world.AddOverlay(
                    RenderCommandBuffer.FillTexture,
                    0,
                    0,
                    fillW,
                    barH - 2,
                    barX + 1,
                    barY + 1,
                    worldY + FrameSize,
                    worldX,
                    8901 + (m.MonsterId * 2),
                    tint: hpTint);
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
