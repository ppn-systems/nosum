// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Farm;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Farm;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the farm on top of a map: tilled soil (dry or wet, joined to its neighbours), the crops as
/// depth-sorted world sprites, and the cursor on the tile in front of the farmer. Crop stages are computed
/// each frame from the mirrored timestamps and the server clock estimate, with the same rules as the server
/// (<see cref="FarmRules"/>). Allocation-free: it reads the mirror under its lock and draws structs.
/// </summary>
internal sealed class FarmLayer
{
    // Index into the 4 x 4 soil sheet by which neighbours are tilled (bit 1 up, 2 right, 4 down, 8 left).
    private static readonly int[] s_soilCell = [0, 12, 13, 9, 4, 8, 1, 5, 15, 11, 14, 10, 3, 7, 2, 6];

    private const int WetSheetOffset = 64;

    private readonly FarmChannel _farm;
    private readonly int _soilTexture;
    private readonly int _cropsTexture;
    private readonly int _cursorTexture;
    private readonly int _tileSize;

    /// <summary>Initializes the layer.</summary>
    /// <param name="farm">The mirrored tiles.</param>
    /// <param name="soilTexture">Texture id of <c>hoeDirt.png</c>.</param>
    /// <param name="cropsTexture">Texture id of <c>crops.png</c>.</param>
    /// <param name="cursorTexture">Texture id of the 16 x 16 target cursor.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public FarmLayer(FarmChannel farm, int soilTexture, int cropsTexture, int cursorTexture, int tileSize)
    {
        _farm = farm;
        _soilTexture = soilTexture;
        _cropsTexture = cropsTexture;
        _cursorTexture = cursorTexture;
        _tileSize = tileSize;
    }

    /// <summary>Draws the tilled soil that intersects the camera.</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="nowMs">The server clock estimate, Unix milliseconds.</param>
    public void DrawSoil(RenderCommandBuffer commands, in Camera2D camera, long nowMs)
    {
        using (_farm.Read())
        {
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
                int sourceX = ((cell & 3) * 16) + (nowMs < tile.WetUntilMs ? WetSheetOffset : 0);
                int sourceY = (cell >> 2) * 16;
                commands.Draw(_soilTexture, sourceX, sourceY, 16, 16, worldX - camera.Left, worldY - camera.Top);
            }
        }
    }

    /// <summary>Adds the crops that intersect the camera to the depth-sorted world batch.</summary>
    /// <param name="world">The world batch.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="nowMs">The server clock estimate, Unix milliseconds.</param>
    public void AddCrops(WorldSpriteBatch world, in Camera2D camera, long nowMs)
    {
        using (_farm.Read())
        {
            for (int i = 0; i < _farm.Count; i++)
            {
                FarmTileInfo info = _farm.TileAt(i);
                if (info.CropId == 0)
                {
                    continue;
                }

                int worldX = info.X * _tileSize;
                int worldY = ((info.Y + 1) * _tileSize) - CropSprites.FrameHeight;
                if (worldX + CropSprites.FrameWidth <= camera.Left || worldY + CropSprites.FrameHeight <= camera.Top || worldX >= camera.Left + camera.Width || worldY >= camera.Top + camera.Height)
                {
                    continue;
                }

                FarmTile tile = new(info.CropId, info.PlantedAtMs, info.GrowthMs, info.SettledAtMs, info.WetUntilMs);
                if (CropSprites.TryGetFrame(info.CropId, FarmRules.StageAt(tile, nowMs), out int sourceX, out int sourceY))
                {
                    // Anchored at the tile's centre so a farmer walking over it is ordered by which half they stand on.
                    world.Add(_cropsTexture, sourceX, sourceY, CropSprites.FrameWidth, CropSprites.FrameHeight, worldX, worldY, (info.Y * _tileSize) + (_tileSize / 2), worldX);
                }
            }
        }
    }

    /// <summary>Draws the cursor on a tile.</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    public void DrawCursor(RenderCommandBuffer commands, in Camera2D camera, int tileX, int tileY)
        => commands.Draw(_cursorTexture, 0, 0, 16, 16, (tileX * _tileSize) - camera.Left, (tileY * _tileSize) - camera.Top);
}
