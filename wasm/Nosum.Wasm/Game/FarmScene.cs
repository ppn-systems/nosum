// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Core.Interpolation;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Movement;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Player;

namespace Nosum.Wasm.Game;

/// <summary>
/// One playable map with one local farmer: fixed-step movement, a camera that follows, and the
/// layer order of docs/adr/ADR-005 (ground, depth-sorted world, front). With a
/// <see cref="PlayerChannel"/> the local farmer is predicted here and checked by the server, and the
/// other farmers on the map are interpolated and depth-sorted with it (docs/adr/ADR-008).
/// </summary>
internal sealed class FarmScene
{
    private const double StepSeconds = 1.0 / 60.0;
    private const double WalkFrameSeconds = 0.15;
    private const int TargetTilesAcross = 26;

    private static readonly string[] s_groundLayers = ["Back", "Buildings"];
    private static readonly string[] s_frontLayers = ["Front", "AlwaysFront", "AlwaysFront2"];

    // The map's own animations cover lily pads and grass, not the pond surface: ripple water tiles.
    private static readonly TileWave s_waterWave = new(TileTraits.Water, StripHeight: 4, Amplitude: 1, Wavelength: 24, PeriodMs: 1600);

    private readonly TileMap _map;
    private readonly int[] _tileSetTextures;
    private readonly FarmerTextures _farmerTextures;
    private readonly WalkGrid _grid;
    private readonly RenderCommandBuffer _commands = new(8192);
    private readonly WorldSpriteBatch _world = new(256);
    private readonly PropLayer _props;
    private readonly TileLayer[] _ground;
    private readonly TileLayer[] _front;
    private FixedTimestep _clock = new(StepSeconds, 0.25);
    private readonly PlayerChannel? _channel;
    private readonly RemotePlayers? _remote;
    private Walker _farmer;
    private Camera2D _camera;
    private uint _ignoreCorrectionsBefore;
    private double _walkTime;
    private double _clockSeconds;

    /// <summary>Initializes the scene.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tileSetTextures">Texture id per map tileset.</param>
    /// <param name="farmerTextures">Farmer sheet texture ids.</param>
    /// <param name="look">The farmer's look.</param>
    /// <param name="props">Buildings and props; their footprints block walking.</param>
    /// <param name="startTileX">Preferred start tile x.</param>
    /// <param name="startTileY">Preferred start tile y.</param>
    /// <param name="channel">Server connection for multiplayer, or <see langword="null"/> to play offline.</param>
    public FarmScene(TileMap map, int[] tileSetTextures, FarmerTextures farmerTextures, FarmerLook look, PropLayer props, int startTileX, int startTileY, PlayerChannel? channel = null)
    {
        _channel = channel;
        _remote = channel is null ? null : new RemotePlayers(channel);
        _map = map;
        _tileSetTextures = tileSetTextures;
        _farmerTextures = farmerTextures;
        this.Look = look;
        _props = props;
        _grid = FarmProps.BuildWalkGrid(map);
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
        this.ApplyServerAnswers();

        _clockSeconds += frameSeconds;
        long timeMs = (long)(_clockSeconds * 1000);
        int steps = _clock.Advance(frameSeconds);
        for (int i = 0; i < steps; i++)
        {
            _farmer.Step(input, StepSeconds, _grid);
        }

        _walkTime = _farmer.IsMoving ? _walkTime + (steps * StepSeconds) : 0;
        _channel?.Report((float)_farmer.X, (float)_farmer.Y, (byte)_farmer.Facing, _farmer.IsMoving);

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
            TileLayerRenderer.Draw(_map, layer, _tileSetTextures, _camera, _commands, timeMs, s_waterWave);
        }

        int walkFrame = _farmer.IsMoving ? 1 + (int)(_walkTime / WalkFrameSeconds) : 0;
        _world.Begin();
        _props.AddToBatch(_world);
        FarmerSprite.Draw(_world, _farmerTextures, this.Look, _farmer.Facing, walkFrame, (int)_farmer.X, (int)_farmer.Y);
        this.AddRemoteFarmers();
        _world.Flush(_commands, _camera);
        _props.DrawFront(_commands, _camera);

        foreach (TileLayer layer in _front)
        {
            TileLayerRenderer.Draw(_map, layer, _tileSetTextures, _camera, _commands, timeMs, s_waterWave);
        }

        backend.Submit(_commands.Encode());
    }

    // Moves the local farmer to the spawn the server chose, or back to where the server keeps it
    // after refusing a move.
    private void ApplyServerAnswers()
    {
        if (_channel is null)
        {
            return;
        }

        if (_channel.TryTakeSpawn(out float spawnX, out float spawnY))
        {
            _farmer = new Walker(spawnX, spawnY);
            _remote?.Reset();
            _ignoreCorrectionsBefore = _channel.LastSentSequence + 1;
        }

        // Intents already in flight when we snap back are refused too; ignore their corrections.
        if (_channel.TryTakeCorrection(out MoveCorrectionInfo correction) && correction.Sequence >= _ignoreCorrectionsBefore)
        {
            _farmer = new Walker(correction.X, correction.Y);
            _ignoreCorrectionsBefore = _channel.LastSentSequence + 1;
        }
    }

    // Adds the other farmers to the same batch as the local one, buildings and props, so one
    // depth sort orders them all (docs/adr/ADR-005 D4). Allocation-free: poses are structs.
    private void AddRemoteFarmers()
    {
        if (_remote is null)
        {
            return;
        }

        double now = RemotePlayers.LocalNow();
        double animation = _remote.AnimationTime(now);
        for (int i = 0; i < _remote.Count; i++)
        {
            uint id = _remote.IdAt(i);
            if (!_remote.TryGetPose(id, now, out EntityPose pose))
            {
                continue;
            }

            bool moving = (pose.State & 4) != 0 && !pose.Held;
            int walk = moving ? 1 + (int)(animation / WalkFrameSeconds) : 0;
            FarmerSprite.Draw(_world, _farmerTextures, RemoteLook.For(id), (Facing)(pose.State & 3), walk, (int)pose.X, (int)pose.Y, (int)id);
        }
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
