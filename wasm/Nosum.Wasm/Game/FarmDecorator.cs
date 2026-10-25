// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Farm;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the farm on a farmable map: tilled soil (dry or wet, joined to its neighbours) on the ground, the crops as
/// depth-sorted world sprites, and the cursor on the tile in front of the farmer. Crop stages are computed each
/// frame from the mirrored timestamps and the server clock estimate with the same rules as the server
/// (<see cref="FarmRules"/>). The mirror holds the tiles of the map the player is on, so a decorator draws only while
/// the mirror's map is its own. Allocation-free: it reads the mirror under its lock and draws structs.
/// </summary>
internal sealed class FarmDecorator : IMapDecorator
{
    // Index into the 4 x 4 soil sheet by which neighbours are tilled (bit 1 up, 2 right, 4 down, 8 left).
    private static readonly int[] s_soilCell = [0, 12, 13, 9, 4, 8, 1, 5, 15, 11, 14, 10, 3, 7, 2, 6];

    private const int WetSheetOffset = 64;

    private readonly string _mapId;
    private readonly FarmChannel _farm;
    private readonly PlayerActions _actions;
    private readonly int _soilTexture;
    private readonly int _cropsTexture;
    private readonly int _cursorTexture;
    private readonly int _tileSize;
    private readonly CalendarChannel? _calendar;
    private GameCalendar? _rules;
    private Camera2D _camera;
    private long _nowMs;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">The map it decorates.</param>
    /// <param name="farm">The mirrored tiles.</param>
    /// <param name="actions">Where the cursor goes.</param>
    /// <param name="soilTexture">Texture id of <c>hoeDirt.png</c>.</param>
    /// <param name="cropsTexture">Texture id of <c>crops.png</c>.</param>
    /// <param name="cursorTexture">Texture id of the 16 x 16 target cursor.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    /// <param name="calendar">The server calendar; crops that withered are not drawn. <see langword="null"/> draws every crop.</param>
    public FarmDecorator(string mapId, FarmChannel farm, PlayerActions actions, int soilTexture, int cropsTexture, int cursorTexture, int tileSize, CalendarChannel? calendar = null)
    {
        _calendar = calendar;
        _mapId = mapId;
        _farm = farm;
        _actions = actions;
        _soilTexture = soilTexture;
        _cropsTexture = cropsTexture;
        _cursorTexture = cursorTexture;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void DrawOnGround(RenderCommandBuffer commands, in Camera2D camera, long timeMs)
    {
        _ = timeMs;
        // Kept for AddToWorld, which runs later in the same frame and has no camera of its own.
        _camera = camera;
        _nowMs = _farm.ServerNowMs();

        using (_farm.Read())
        {
            if (!string.Equals(_farm.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            for (int i = 0; i < _farm.Count; i++)
            {
                FarmTileInfo tile = _farm.TileAt(i);
                int worldX = tile.X * _tileSize;
                int worldY = tile.Y * _tileSize;
                if (worldX + _tileSize <= camera.Left || worldY + _tileSize <= camera.Top || worldX >= camera.Left + camera.Width || worldY >= camera.Top + camera.Height)
                {
                    continue;
                }

                int mask = (_farm.TryGetTile(tile.X, tile.Y - 1, out _) ? 1 : 0)
                    | (_farm.TryGetTile(tile.X + 1, tile.Y, out _) ? 2 : 0)
                    | (_farm.TryGetTile(tile.X, tile.Y + 1, out _) ? 4 : 0)
                    | (_farm.TryGetTile(tile.X - 1, tile.Y, out _) ? 8 : 0);
                int cell = s_soilCell[mask];
                int sourceX = ((cell & 3) * 16) + (_nowMs < tile.WetUntilMs ? WetSheetOffset : 0);
                int sourceY = (cell >> 2) * 16;
                commands.Draw(_soilTexture, sourceX, sourceY, 16, 16, worldX - camera.Left, worldY - camera.Top);
            }
        }

        commands.Draw(_cursorTexture, 0, 0, 16, 16, (_actions.TargetX * _tileSize) - camera.Left, (_actions.TargetY * _tileSize) - camera.Top);
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        GameCalendar? rules = this.Rules();
        using (_farm.Read())
        {
            if (!string.Equals(_farm.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            for (int i = 0; i < _farm.Count; i++)
            {
                FarmTileInfo info = _farm.TileAt(i);
                if (info.CropId == 0)
                {
                    continue;
                }

                int worldX = info.X * _tileSize;
                int worldY = ((info.Y + 1) * _tileSize) - CropSprites.FrameHeight;
                if (worldX + CropSprites.FrameWidth <= _camera.Left || worldY + CropSprites.FrameHeight <= _camera.Top || worldX >= _camera.Left + _camera.Width || worldY >= _camera.Top + _camera.Height)
                {
                    continue;
                }

                FarmTile tile = new(info.CropId, info.PlantedAtMs, info.GrowthMs, info.SettledAtMs, info.WetUntilMs);

                // A crop that withered at the season change is gone from the picture; the soil stays and can be planted over (docs/adr/ADR-018).
                if (rules is not null && FarmRules.IsWithered(tile, _nowMs, rules))
                {
                    continue;
                }

                if (CropSprites.TryGetFrame(info.CropId, FarmRules.StageAt(tile, _nowMs), out int sourceX, out int sourceY))
                {
                    // Anchored at the tile's centre so a farmer walking over it is ordered by which half they stand on.
                    world.Add(_cropsTexture, sourceX, sourceY, CropSprites.FrameWidth, CropSprites.FrameHeight, worldX, worldY, (info.Y * _tileSize) + (_tileSize / 2), worldX);
                }
            }
        }
    }

    // The calendar the server sent, rebuilt only when its numbers change (once, at sign-in); null until it is known.
    private GameCalendar? Rules()
    {
        if (_calendar is not { IsLoaded: true })
        {
            return null;
        }

        if (_rules is null || _rules.EpochMs != _calendar.EpochMs || _rules.DayLengthMs != _calendar.DayLengthMs)
        {
            _rules = new GameCalendar(_calendar.EpochMs, _calendar.DayLengthMs);
        }

        return _rules;
    }
}

/// <summary>Builds the <see cref="FarmDecorator"/> of every farmable map.</summary>
internal sealed class FarmDecoratorProvider : IMapDecoratorProvider
{
    private const string TileSheetsUrl = "assets/TileSheets/";
    private const string TerrainUrl = "assets/TerrainFeatures/";

    private readonly FarmChannel _farm;
    private readonly PlayerActions _actions;
    private readonly CalendarChannel? _calendar;

    /// <summary>Initializes the provider.</summary>
    /// <param name="farm">The mirrored tiles.</param>
    /// <param name="actions">Where the cursor goes.</param>
    /// <param name="calendar">The server calendar, so withered crops are not drawn.</param>
    public FarmDecoratorProvider(FarmChannel farm, PlayerActions actions, CalendarChannel? calendar = null)
    {
        _calendar = calendar;
        _farm = farm;
        _actions = actions;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);

        if (!spec.Farmable)
        {
            return null;
        }

        int[] ids = await Task.WhenAll(
            textures.LoadAsync(TerrainUrl + "hoeDirt.png"),
            textures.LoadAsync(TileSheetsUrl + "crops.png"),
            textures.LoadAsync(TargetCursor.DataUrl)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new FarmDecorator(spec.Id, _farm, _actions, ids[0], ids[1], ids[2], map.TileWidth, _calendar);
    }
}
