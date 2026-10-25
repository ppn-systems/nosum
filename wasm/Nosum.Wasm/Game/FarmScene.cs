// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Movement;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// One playable map with one local farmer: fixed-step movement, a camera that follows, and the
/// layer order of docs/adr/ADR-005 (ground, buildings, player, front).
/// </summary>
internal sealed class FarmScene
{
    private const double StepSeconds = 1.0 / 60.0;
    private const double WalkFrameSeconds = 0.15;
    private const int TargetTilesAcross = 26;

    private static readonly string[] s_groundLayers = ["Back", "Buildings"];
    private static readonly string[] s_frontLayers = ["Front", "AlwaysFront", "AlwaysFront2"];

    private readonly TileMap _map;
    private readonly int[] _tileSetTextures;
    private readonly FarmerTextures _farmerTextures;
    private readonly WalkGrid _grid;
    private readonly RenderCommandBuffer _commands = new(8192);
    private readonly TileLayer[] _ground;
    private readonly TileLayer[] _front;
    private FixedTimestep _clock = new(StepSeconds, 0.25);
    private Walker _farmer;
    private Camera2D _camera;
    private double _walkTime;

    /// <summary>Initializes the scene.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tileSetTextures">Texture id per map tileset.</param>
    /// <param name="farmerTextures">Farmer sheet texture ids.</param>
    /// <param name="look">The farmer's look.</param>
    /// <param name="startTileX">Preferred start tile x.</param>
    /// <param name="startTileY">Preferred start tile y.</param>
    public FarmScene(TileMap map, int[] tileSetTextures, FarmerTextures farmerTextures, FarmerLook look, int startTileX, int startTileY)
    {
        _map = map;
        _tileSetTextures = tileSetTextures;
        _farmerTextures = farmerTextures;
        this.Look = look;
        _grid = WalkGrid.FromLayer(map, "Buildings");
        _ground = Layers(map, s_groundLayers);
        _front = Layers(map, s_frontLayers);

        (int tx, int ty) = NearestWalkable(_grid, startTileX, startTileY);
        _farmer = new Walker((tx + 0.5) * map.TileWidth, (ty + 1) * map.TileHeight - 1);
    }

    /// <summary>Gets or sets what the farmer wears.</summary>
    public FarmerLook Look { get; set; }

    /// <summary>
    /// Advances the simulation and encodes the frame.
    /// </summary>
    /// <param name="frameSeconds">Wall time since the previous frame.</param>
    /// <param name="input">Held directions.</param>
    /// <param name="canvasWidth">Canvas width, CSS pixels.</param>
    /// <param name="canvasHeight">Canvas height, CSS pixels.</param>
    /// <param name="backend">Where the frame is drawn.</param>
    public void Frame(double frameSeconds, InputState input, int canvasWidth, int canvasHeight, IRenderBackend backend)
    {
        int steps = _clock.Advance(frameSeconds);
        for (int i = 0; i < steps; i++)
        {
            _farmer.Step(input, StepSeconds, _grid);
        }

        _walkTime = _farmer.IsMoving ? _walkTime + (steps * StepSeconds) : 0;

        int scale = Math.Clamp((int)Math.Round(canvasWidth / (double)(TargetTilesAcross * _map.TileWidth)), 2, 6);
        _camera.Follow(
            (int)_farmer.X,
            (int)_farmer.Y - (FarmerSprite.Height / 2),
            (canvasWidth + scale - 1) / scale,
            (canvasHeight + scale - 1) / scale,
            _map.Width * _map.TileWidth,
            _map.Height * _map.TileHeight);

        _commands.Begin(scale);
        foreach (TileLayer layer in _ground)
        {
            TileLayerRenderer.Draw(_map, layer, _tileSetTextures, _camera, _commands);
        }

        int walkFrame = _farmer.IsMoving ? 1 + (int)(_walkTime / WalkFrameSeconds) : 0;
        FarmerSprite.Draw(_commands, _farmerTextures, this.Look, _farmer.Facing, walkFrame, (int)_farmer.X - _camera.Left, (int)_farmer.Y - _camera.Top);

        foreach (TileLayer layer in _front)
        {
            TileLayerRenderer.Draw(_map, layer, _tileSetTextures, _camera, _commands);
        }

        backend.Submit(_commands.Encode());
    }

    private static TileLayer[] Layers(TileMap map, string[] names)
    {
        int count = 0;
        TileLayer[] found = new TileLayer[names.Length];
        foreach (string name in names)
        {
            if (map.FindLayer(name) is TileLayer layer)
            {
                found[count++] = layer;
            }
        }

        return found[..count];
    }

    private static (int X, int Y) NearestWalkable(WalkGrid grid, int x, int y)
    {
        for (int radius = 0; radius < Math.Max(grid.Width, grid.Height); radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (!grid.IsBlocked(x + dx, y + dy))
                    {
                        return (x + dx, y + dy);
                    }
                }
            }
        }

        return (x, y);
    }
}
