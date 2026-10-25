// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Core.Interpolation;
using Nosum.Gameplay.Movement;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Player;

namespace Nosum.Wasm.Game;

/// <summary>
/// One playable map with one local farmer: fixed-step movement, a camera that follows, and the
/// layer order of docs/adr/ADR-005 (ground, buildings, player, front). With a
/// <see cref="PlayerChannel"/> the local farmer is predicted here and checked by the server, and the
/// other farmers on the map are drawn interpolated and y-sorted with it (docs/adr/ADR-008).
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
    private readonly PlayerChannel? _channel;
    private readonly RemotePlayers? _remote;
    private readonly DrawItem[] _items = new DrawItem[RemotePlayers.MaxPlayers + 1];
    private Walker _farmer;
    private Camera2D _camera;
    private double _walkTime;
    private uint _ignoreCorrectionsBefore;

    /// <summary>Initializes the scene.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tileSetTextures">Texture id per map tileset.</param>
    /// <param name="farmerTextures">Farmer sheet texture ids.</param>
    /// <param name="look">The farmer's look.</param>
    /// <param name="startTileX">Preferred start tile x.</param>
    /// <param name="startTileY">Preferred start tile y.</param>
    /// <param name="channel">Server connection for multiplayer, or <see langword="null"/> to play offline.</param>
    public FarmScene(TileMap map, int[] tileSetTextures, FarmerTextures farmerTextures, FarmerLook look, int startTileX, int startTileY, PlayerChannel? channel = null)
    {
        _channel = channel;
        _remote = channel is null ? null : new RemotePlayers(channel);
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
        this.ApplyServerAnswers();

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
            TileLayerRenderer.Draw(_map, layer, _tileSetTextures, _camera, _commands);
        }

        this.DrawFarmers((canvasWidth + scale - 1) / scale);

        foreach (TileLayer layer in _front)
        {
            TileLayerRenderer.Draw(_map, layer, _tileSetTextures, _camera, _commands);
        }

        backend.Submit(_commands.Encode());
    }

    // Moves the local farmer to the spawn the server chose, or back to where it keeps the farmer
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

        // Intents already in flight when we snap back would be refused too; ignore their corrections.
        if (_channel.TryTakeCorrection(out MoveCorrectionInfo correction) && correction.Sequence >= _ignoreCorrectionsBefore)
        {
            _farmer = new Walker(correction.X, correction.Y);
            _ignoreCorrectionsBefore = _channel.LastSentSequence + 1;
        }
    }

    // Collects the local and remote farmers, sorts them by anchor y (docs/adr/ADR-005 D4) and
    // draws them; the buffers are preallocated, so this allocates nothing.
    private void DrawFarmers(int viewWidth)
    {
        int count = 0;
        int localWalk = _farmer.IsMoving ? 1 + (int)(_walkTime / WalkFrameSeconds) : 0;
        _items[count++] = new DrawItem(0, _farmer.X, _farmer.Y, _farmer.Facing, localWalk, this.Look);

        if (_remote is not null)
        {
            double now = RemotePlayers.LocalNow();
            double animation = _remote.AnimationTime(now);
            for (int i = 0; i < _remote.Count && count < _items.Length; i++)
            {
                uint id = _remote.IdAt(i);
                if (!_remote.TryGetPose(id, now, out EntityPose pose))
                {
                    continue;
                }

                if (pose.X < _camera.Left - FarmerSprite.Width || pose.X > _camera.Left + viewWidth + FarmerSprite.Width)
                {
                    continue;
                }

                bool moving = (pose.State & 4) != 0 && !pose.Held;
                int walk = moving ? 1 + (int)(animation / WalkFrameSeconds) : 0;
                _items[count++] = new DrawItem(id, pose.X, pose.Y, (Facing)(pose.State & 3), walk, RemoteLook.For(id));
            }
        }

        for (int i = 1; i < count; i++)
        {
            DrawItem item = _items[i];
            int j = i - 1;
            while (j >= 0 && item.CompareTo(_items[j]) < 0)
            {
                _items[j + 1] = _items[j];
                j--;
            }

            _items[j + 1] = item;
        }

        for (int i = 0; i < count; i++)
        {
            ref readonly DrawItem item = ref _items[i];
            FarmerSprite.Draw(_commands, _farmerTextures, item.Look, item.Facing, item.WalkFrame, (int)item.X - _camera.Left, (int)item.Y - _camera.Top);
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

/// <summary>One farmer queued for drawing; ordered by anchor y, then x, then id.</summary>
internal readonly record struct DrawItem(uint Id, double X, double Y, Facing Facing, int WalkFrame, FarmerLook Look)
{
    /// <summary>Compares the draw order.</summary>
    /// <param name="other">The other item.</param>
    /// <returns>Negative when this item is drawn first.</returns>
    public int CompareTo(DrawItem other)
    {
        int byY = this.Y.CompareTo(other.Y);
        if (byY != 0)
        {
            return byY;
        }

        int byX = this.X.CompareTo(other.X);
        return byX != 0 ? byX : this.Id.CompareTo(other.Id);
    }
}
