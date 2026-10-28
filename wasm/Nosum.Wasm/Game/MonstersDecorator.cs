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
    private readonly int _sheetTexture;
    private readonly int _itemsTexture;
    private readonly List<MonsterClientRow> _scratch = new(16);
    private readonly List<ProjectileClientRow> _projectilesScratch = new(16);

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">Map id being decorated.</param>
    /// <param name="combat">The client combat channel.</param>
    /// <param name="sheetTexture">Texture id of <c>assets/sprites/monsters.png</c>.</param>
    /// <param name="itemsTexture">Texture id of <c>assets/sprites/items.png</c>.</param>
    public MonstersDecorator(string mapId, CombatChannel combat, int sheetTexture, int itemsTexture)
    {
        _mapId = mapId;
        _combat = combat;
        _sheetTexture = sheetTexture;
        _itemsTexture = itemsTexture;
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
    }
}

/// <summary>Builds <see cref="MonstersDecorator"/> instances for maps.</summary>
internal sealed class MonstersDecoratorProvider : IMapDecoratorProvider
{
    private readonly CombatChannel _combat;

    /// <summary>Initializes the provider.</summary>
    /// <param name="combat">The combat channel.</param>
    public MonstersDecoratorProvider(CombatChannel combat) => _combat = combat;

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        int sheet = await textures.LoadAsync(MonstersDecorator.SheetUrl).ConfigureAwait(false);
        int itemsSheet = await textures.LoadAsync(MonstersDecorator.ItemsSheetUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _combat.LoadMonsters();
        return new MonstersDecorator(spec.Id, _combat, sheet, itemsSheet);
    }
}
