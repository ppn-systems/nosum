// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Coop;

namespace Nosum.Wasm.Game;

/// <summary>
/// Shades the part of the co-op land that the co-op has not unlocked yet (docs/adr/ADR-038 D8): walkable, but the server refuses tilling and placing there. The unlocked
/// area comes from the same <see cref="CoopUpgrades"/> rectangles as the server's rule and the level from the co-op snapshot. Drawn as one rectangle per run of
/// locked tiles in a row, so a frame records a few dozen draws and allocates nothing.
/// </summary>
internal sealed class LockedLandDecorator : IMapDecorator
{
    // Dark blue-grey at about 45 % opacity: the ground shows through, so the player sees what will be theirs.
    private const int Shade = 0x141C3C73;

    private readonly CoopChannel _coop;
    private readonly int _mapWidth;
    private readonly int _mapHeight;
    private readonly int _tileSize;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="coop">The co-op channel, which holds the land level.</param>
    /// <param name="mapWidth">Map width, tiles.</param>
    /// <param name="mapHeight">Map height, tiles.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public LockedLandDecorator(CoopChannel coop, int mapWidth, int mapHeight, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(coop);

        _coop = coop;
        _mapWidth = mapWidth;
        _mapHeight = mapHeight;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void DrawOnGround(RenderCommandBuffer commands, in Camera2D camera, long timeMs)
    {
        _ = timeMs;
        CoopInfo info = _coop.Current;
        if (!info.HasCoop || info.LandLevel >= CoopUpgrades.MaxLevel)
        {
            return;
        }

        int level = info.LandLevel;
        int firstRow = Math.Max(0, camera.Top / _tileSize);
        int lastRow = Math.Min(_mapHeight - 1, (camera.Top + camera.Height) / _tileSize);
        int firstColumn = Math.Max(0, camera.Left / _tileSize);
        int lastColumn = Math.Min(_mapWidth - 1, (camera.Left + camera.Width) / _tileSize);
        for (int y = firstRow; y <= lastRow; y++)
        {
            int x = firstColumn;
            while (x <= lastColumn)
            {
                if (CoopUpgrades.IsLandUnlocked(level, x, y))
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x <= lastColumn && !CoopUpgrades.IsLandUnlocked(level, x, y))
                {
                    x++;
                }

                commands.FillRect((start * _tileSize) - camera.Left, (y * _tileSize) - camera.Top, (x - start) * _tileSize, _tileSize, Shade);
            }
        }
    }
}

/// <summary>Builds the <see cref="LockedLandDecorator"/> of the co-op land.</summary>
internal sealed class LockedLandDecoratorProvider : IMapDecoratorProvider
{
    private readonly CoopChannel _coop;

    /// <summary>Initializes the provider.</summary>
    /// <param name="coop">The co-op channel.</param>
    public LockedLandDecoratorProvider(CoopChannel coop)
    {
        ArgumentNullException.ThrowIfNull(coop);
        _coop = coop;
    }

    /// <inheritdoc/>
    public Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);

        return Task.FromResult<IMapDecorator?>(spec.IsCoop ? new LockedLandDecorator(_coop, map.Width, map.Height, map.TileWidth) : null);
    }
}
