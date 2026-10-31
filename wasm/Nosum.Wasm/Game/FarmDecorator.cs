// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Placeable;

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

    // The reticle's multiply tint when the held item would do nothing on the tile in front (0xRRGGBBAA): red, the rest dimmed.
    private static readonly int s_deniedTint = unchecked((int)0xFF7070FF);

    // The multiply tint of a thirsty tile, one dry day from withering or reverting (0xRRGGBBAA): parched yellow, the blue dimmed.
    private static readonly int s_thirstyTint = unchecked((int)0xE6C070FF);

    private readonly string _mapId;
    private readonly FarmChannel _farm;
    private readonly PlayerActions _actions;
    private readonly int _soilTexture;
    private readonly int _cropsTexture;
    private readonly int _cursorTexture;
    private readonly int _tileSize;
    private readonly CalendarChannel? _calendar;
    private readonly bool _outdoors;
    private readonly PlaceableChannel? _placed;
    private GameCalendar? _rules;
    private Camera2D _camera;
    private long _nowMs;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">The map it decorates.</param>
    /// <param name="farm">The mirrored tiles.</param>
    /// <param name="actions">Where the cursor goes.</param>
    /// <param name="soilTexture">Texture id of <c>soil.png</c>.</param>
    /// <param name="cropsTexture">Texture id of <c>crops.png</c>.</param>
    /// <param name="cursorTexture">Texture id of the 16 x 16 target cursor.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    /// <param name="calendar">The server calendar: it tells the game day, which decides growth, wet soil and withering (docs/adr/ADR-018, ADR-021 D7). Without it nothing grows on screen.</param>
    /// <param name="outdoors">Whether the map is outdoors, where rain waters the soil and the crops (docs/adr/ADR-027 D2).</param>
    /// <param name="placed">The placed pieces mirror: a sprinkler beside a tile waters it every day (docs/adr/ADR-024), so the soil and the crop show it.</param>
    public FarmDecorator(string mapId, FarmChannel farm, PlayerActions actions, int soilTexture, int cropsTexture, int cursorTexture, int tileSize, CalendarChannel? calendar = null, bool outdoors = true, PlaceableChannel? placed = null)
    {
        _placed = placed;
        _outdoors = outdoors;
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
        GameCalendar? rules = this.Rules();

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
                long? sinceMs = this.SprinklerSinceMs(tile.X, tile.Y);
                FarmTile soil = new(tile.CropId, tile.PlantedAtMs, tile.GrowthDays, tile.WateredDay, tile.TendedDay);
                bool wet = rules is not null && FarmRules.IsWet(soil, _nowMs, rules, _outdoors, sinceMs);

                // Bare soil one dry day from reverting reads thirsty (docs/adr/ADR-021 D7); a planted tile shows its thirst on the crop instead.
                bool thirsty = tile.CropId == 0 && rules is not null && FarmRules.IsAtRisk(soil, _nowMs, rules, _outdoors, sinceMs);
                int sourceX = ((cell & 3) * 16) + (wet ? WetSheetOffset : 0);
                int sourceY = (cell >> 2) * 16;
                commands.Draw(_soilTexture, sourceX, sourceY, 16, 16, worldX - camera.Left, worldY - camera.Top, 0, thirsty ? s_thirstyTint : RenderCommandBuffer.NoTint);
            }
        }

        // The reticle turns red when the held seed, hoe or watering can would do nothing on the tile in front (PlayerActions.TargetDenied).
        int reticleTint = _actions.TargetDenied ? s_deniedTint : RenderCommandBuffer.NoTint;
        commands.Draw(_cursorTexture, 0, 0, 16, 16, (_actions.TargetX * _tileSize) - camera.Left, (_actions.TargetY * _tileSize) - camera.Top, 0, reticleTint);
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        // Growth counts game days, which only the calendar can tell; until the server sent it (once, at sign-in) no crop is drawn.
        GameCalendar? rules = this.Rules();
        if (rules is null)
        {
            return;
        }

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

                FarmTile tile = new(info.CropId, info.PlantedAtMs, info.GrowthDays, info.WateredDay, info.TendedDay);
                long? sinceMs = this.SprinklerSinceMs(info.X, info.Y);

                // A crop that withered shows as a wilted brown stalk until cleared with a tool or replanted over (docs/adr/ADR-018). The server says
                // whether it has withered (the tile row's flag, sprinklers counted), so the client does not guess it.
                bool withered = info.Withered;
                int stage = withered ? 1 : FarmRules.StageAt(tile, _nowMs, rules, _outdoors, sinceMs);

                // A crop one dry day from withering reads thirsty until it is watered (docs/adr/ADR-021 D7).
                bool thirsty = !withered && FarmRules.IsAtRisk(tile, _nowMs, rules, _outdoors, sinceMs);
                int cropTint = withered ? unchecked((int)0x8b6b4aff) : thirsty ? s_thirstyTint : RenderCommandBuffer.NoTint;
                if (CropSprites.TryGetFrame(info.CropId, stage, out int sourceX, out int sourceY))
                {
                    // Anchored at the tile's centre so a farmer walking over it is ordered by which half they stand on.
                    world.Add(_cropsTexture, sourceX, sourceY, CropSprites.FrameWidth, CropSprites.FrameHeight, worldX, worldY, (info.Y * _tileSize) + (_tileSize / 2), worldX, tint: cropTint);
                }
            }
        }
    }

    // When the first placed sprinkler that waters the tile was placed, or null (SprinklerRules.WateredSinceMs, read from the mirror without allocating).
    private long? SprinklerSinceMs(int tileX, int tileY)
    {
        if (_placed is null)
        {
            return null;
        }

        using (_placed.Read())
        {
            if (!string.Equals(_placed.MapId, _mapId, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return _placed.SprinklerWateredSinceMs(tileX, tileY);
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
    private const string SpritesUrl = "assets/sprites/";

    private readonly FarmChannel _farm;
    private readonly PlayerActions _actions;
    private readonly CalendarChannel? _calendar;
    private readonly PlaceableChannel? _placed;

    /// <summary>Initializes the provider.</summary>
    /// <param name="farm">The mirrored tiles.</param>
    /// <param name="actions">Where the cursor goes.</param>
    /// <param name="calendar">The server calendar, so withered crops are not drawn.</param>
    /// <param name="placed">The placed pieces mirror, so soil and crops beside a sprinkler show it.</param>
    public FarmDecoratorProvider(FarmChannel farm, PlayerActions actions, CalendarChannel? calendar = null, PlaceableChannel? placed = null)
    {
        _placed = placed;
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
            textures.LoadAsync(SpritesUrl + "soil.png"),
            textures.LoadAsync(SpritesUrl + "crops.png"),
            textures.LoadAsync(TargetCursor.DataUrl)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new FarmDecorator(spec.Id, _farm, _actions, ids[0], ids[1], ids[2], map.TileWidth, _calendar, !spec.Indoors, _placed);
    }
}
