// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Core.Interpolation;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Player;

namespace Nosum.Wasm.Game;

/// <summary>
/// The playable world: one map at a time with one local farmer. Fixed-step movement, a camera that follows, and the
/// layer order of docs/adr/ADR-005 (ground, depth-sorted world, front). Any map of <see cref="GameMaps"/> works: the scene
/// draws its layers and whatever its <see cref="IMapDecorator"/>s add (buildings now; crops, soil later). With a
/// <see cref="PlayerChannel"/> the local farmer is predicted here and checked by the server, the other farmers on the map
/// are interpolated and depth-sorted with it (docs/adr/ADR-008), and the server changes the map: the scene fades out, waits
/// (without blocking the frame) until <see cref="MapLoader"/> has the new map, swaps, and fades in (docs/adr/ADR-012).
/// </summary>
internal sealed class MapScene
{
    private const double StepSeconds = 1.0 / 60.0;
    private const double WalkFrameSeconds = 0.15;
    private const int TargetTilesAcross = 26;
    private const int MaxScale = 6;

    /// <summary>Seconds the screen takes to go black when the server changes the map.</summary>
    public const double FadeOutSeconds = 0.2;

    /// <summary>Seconds the new map takes to appear.</summary>
    public const double FadeInSeconds = 0.25;

    // The map's own animations cover lily pads and grass, not the pond surface: ripple water tiles.
    private static readonly TileWave s_waterWave = new(TileTraits.Water, StripHeight: 4, Amplitude: 1, Wavelength: 24, PeriodMs: 1600);

    private readonly MapLoader _loader;
    private readonly FarmerTextures _farmerTextures;
    private readonly RenderCommandBuffer _commands = new(8192);
    private readonly WorldSpriteBatch _world = new(256);
    private readonly PlayerChannel? _channel;
    private readonly RemotePlayers? _remote;
    private readonly PlayerActions? _actions;
    private FixedTimestep _clock = new(StepSeconds, 0.25);
    private MapContent _content;
    private Walker _farmer;
    private Camera2D _camera;
    private Phase _phase;
    private double _fade;
    private MapEntry _pending;
    private uint _ignoreCorrectionsBefore;
    private double _walkTime;
    private double _clockSeconds;

    /// <summary>Initializes the scene.</summary>
    /// <param name="loader">Loads the maps the server sends the player to.</param>
    /// <param name="content">The map to start on.</param>
    /// <param name="farmerTextures">Farmer sheet texture ids.</param>
    /// <param name="look">The farmer's look.</param>
    /// <param name="start">Where to start: the preferred tile, or the place the server chose.</param>
    /// <param name="channel">Server connection for multiplayer, or <see langword="null"/> to play offline (no map changes).</param>
    /// <param name="actions">Number keys, the action key and the inventory key; <see langword="null"/> when there is no server to act on.</param>
    public MapScene(MapLoader loader, MapContent content, FarmerTextures farmerTextures, FarmerLook look, MapStart start, PlayerChannel? channel = null, PlayerActions? actions = null)
    {
        _actions = actions;
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(content);

        _loader = loader;
        _channel = channel;
        _remote = channel is null ? null : new RemotePlayers(channel);
        _farmerTextures = farmerTextures;
        this.Look = look;
        _content = content;
        _farmer = StartWalker(content, start);
        this.PrefetchNeighbours();
    }

    private enum Phase
    {
        Playing,
        FadingOut,
        FadingIn,
    }

    /// <summary>Gets or sets what the farmer wears.</summary>
    public FarmerLook Look { get; set; }

    /// <summary>Gets the id of the map being shown (the old one until a map change has faded out).</summary>
    public string MapId => _content.Spec.Id;

    /// <summary>Gets the opacity of the black fade over the scene, 0 (clear) to 1.</summary>
    public double Fade => _fade;

    /// <summary>Gets whether a map change is in progress and the farmer cannot be moved.</summary>
    public bool IsChangingMap => _phase == Phase.FadingOut;

    /// <summary>
    /// Advances the simulation and encodes the frame.
    /// </summary>
    /// <param name="frameSeconds">Wall time since the previous frame.</param>
    /// <param name="input">Held directions.</param>
    /// <param name="edges">Keys pressed since the previous frame.</param>
    /// <param name="canvasWidth">Canvas width, CSS pixels.</param>
    /// <param name="canvasHeight">Canvas height, CSS pixels.</param>
    /// <param name="backend">Where the frame is drawn.</param>
    public void Frame(double frameSeconds, InputState input, InputEdges edges, int canvasWidth, int canvasHeight, IRenderBackend backend)
    {
        this.ApplyServerAnswers();
        this.AdvanceFade(frameSeconds);

        MapContent content = _content;
        TileMap map = content.Map;
        _clockSeconds += frameSeconds;
        long timeMs = (long)(_clockSeconds * 1000);

        if (_phase == Phase.FadingOut)
        {
            // Frozen while the server's new map loads: no steps, no reports, the old map still on screen.
            _clock.Advance(frameSeconds);
        }
        else
        {
            int steps = _clock.Advance(frameSeconds);
            for (int i = 0; i < steps; i++)
            {
                _farmer.Step(input, StepSeconds, content.Grid);
            }

            _walkTime = _farmer.IsMoving ? _walkTime + (steps * StepSeconds) : 0;
            _channel?.Report((float)_farmer.X, (float)_farmer.Y, (byte)_farmer.Facing, _farmer.IsMoving);
        }

        // Keys act on the map shown, and not while a map change freezes the farmer.
        _actions?.Update(_phase == Phase.FadingOut ? InputEdges.None : edges, _farmer.X, _farmer.Y, _farmer.Facing, map.TileWidth, content.Spec.Farmable, _clockSeconds);

        int scale = ScaleFor(map, canvasWidth, canvasHeight);
        _camera.Follow(
            (int)_farmer.X,
            (int)_farmer.Y - (FarmerSprite.Height / 2),
            (canvasWidth + scale - 1) / scale,
            (canvasHeight + scale - 1) / scale,
            map.Width * map.TileWidth,
            map.Height * map.TileHeight);

        _commands.Begin(scale);
        foreach (TileLayer layer in content.Layers.Ground)
        {
            TileLayerRenderer.Draw(map, layer, content.TileSetTextures, _camera, _commands, timeMs, s_waterWave);
        }

        IMapDecorator[] decorators = content.Decorators;
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].DrawOnGround(_commands, _camera, timeMs);
        }

        int walkFrame = _farmer.IsMoving ? 1 + (int)(_walkTime / WalkFrameSeconds) : 0;
        _world.Begin();
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].AddToWorld(_world);
        }

        FarmerSprite.Draw(_world, _farmerTextures, this.Look, _farmer.Facing, walkFrame, (int)_farmer.X, (int)_farmer.Y);
        if (_phase != Phase.FadingOut)
        {
            this.AddRemoteFarmers();
        }

        _world.Flush(_commands, _camera);
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].DrawInFront(_commands, _camera);
        }

        foreach (TileLayer layer in content.Layers.Front)
        {
            TileLayerRenderer.Draw(map, layer, content.TileSetTextures, _camera, _commands, timeMs, s_waterWave);
        }

        _commands.FillBlack((int)(_fade * 255), (canvasWidth + scale - 1) / scale, (canvasHeight + scale - 1) / scale);
        backend.Submit(_commands.Encode());
    }

    /// <summary>
    /// Picks the whole-number zoom: about <see cref="TargetTilesAcross"/> tiles across, and for a map smaller than that
    /// (a room) as large as still shows all of it.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="canvasWidth">Canvas width, CSS pixels.</param>
    /// <param name="canvasHeight">Canvas height, CSS pixels.</param>
    /// <returns>The scale, 2 to 6.</returns>
    internal static int ScaleFor(TileMap map, int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(map);

        int scale = Math.Clamp((int)Math.Round(canvasWidth / (double)(TargetTilesAcross * map.TileWidth)), 2, MaxScale);
        int fitsWhole = (int)Math.Min(canvasWidth / (double)(map.Width * map.TileWidth), canvasHeight / (double)(map.Height * map.TileHeight));
        return Math.Clamp(Math.Max(scale, fitsWhole), 2, MaxScale);
    }

    private static Walker StartWalker(MapContent content, MapStart start)
    {
        if (start.Exact)
        {
            return new Walker(start.X, start.Y, (Facing)(start.Facing & 3));
        }

        (double x, double y) = SpawnPoint.Find(content.Grid, start.TileX, start.TileY);
        return new Walker(x, y);
    }

    // Takes what the server decided: where to stand after a join or a map change, and corrections of refused moves.
    private void ApplyServerAnswers()
    {
        if (_channel is null)
        {
            return;
        }

        if (_channel.TryTakeEntry(out MapEntry entry))
        {
            // Intents already in flight when the place changes are refused too; ignore their corrections.
            _ignoreCorrectionsBefore = _channel.LastSentSequence + 1;
            if (_phase == Phase.Playing && string.Equals(entry.MapId, _content.Spec.Id, StringComparison.Ordinal))
            {
                // Same map (a join after a reconnect): stand where the server says.
                _farmer = new Walker(entry.X, entry.Y, (Facing)(entry.Facing & 3));
                _remote?.Reset();
            }
            else
            {
                // Another map: black out, load it in the background, swap when it is there.
                _pending = entry;
                _phase = Phase.FadingOut;
                _remote?.Reset();
                _loader.Prefetch(entry.MapId);
            }
        }

        if (_phase != Phase.FadingOut && _channel.TryTakeCorrection(out MoveCorrectionInfo correction) && correction.Sequence >= _ignoreCorrectionsBefore)
        {
            _farmer = new Walker(correction.X, correction.Y);
            _ignoreCorrectionsBefore = _channel.LastSentSequence + 1;
        }
    }

    private void AdvanceFade(double frameSeconds)
    {
        switch (_phase)
        {
            case Phase.FadingOut:
                _fade = Math.Min(1, _fade + (frameSeconds / FadeOutSeconds));
                if (_fade >= 1)
                {
                    // Keep asking: a failed load is retried by the loader after a pause. Never wait here.
                    _loader.Prefetch(_pending.MapId);
                    if (_loader.TryGet(_pending.MapId, out MapContent? next))
                    {
                        this.SwapTo(next);
                    }
                }

                break;

            case Phase.FadingIn:
                _fade = Math.Max(0, _fade - (frameSeconds / FadeInSeconds));
                if (_fade <= 0)
                {
                    _phase = Phase.Playing;
                }

                break;

            default:
                break;
        }
    }

    private void SwapTo(MapContent next)
    {
        _content = next;
        _farmer = new Walker(_pending.X, _pending.Y, (Facing)(_pending.Facing & 3));
        _walkTime = 0;
        _phase = Phase.FadingIn;
        this.PrefetchNeighbours();
    }

    // Loads the maps the exits of this one lead to while the player is busy here, so the next change fades only briefly.
    private void PrefetchNeighbours()
    {
        foreach (MapWarp warp in _content.Warps)
        {
            _loader.Prefetch(warp.TargetMap);
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
}

/// <summary>Where the farmer starts on the first map.</summary>
internal readonly record struct MapStart
{
    /// <summary>Gets the preferred tile x, used when <see cref="Exact"/> is false.</summary>
    public int TileX { get; init; }

    /// <summary>Gets the preferred tile y, used when <see cref="Exact"/> is false.</summary>
    public int TileY { get; init; }

    /// <summary>Gets whether <see cref="X"/>, <see cref="Y"/> and <see cref="Facing"/> are the place the server chose.</summary>
    public bool Exact { get; init; }

    /// <summary>Gets the feet x the server chose, world pixels.</summary>
    public float X { get; init; }

    /// <summary>Gets the feet y the server chose, world pixels.</summary>
    public float Y { get; init; }

    /// <summary>Gets the facing the server chose.</summary>
    public byte Facing { get; init; }

    /// <summary>Starts near a tile, for offline play or when no answer came from the server.</summary>
    /// <param name="tileX">Preferred tile x.</param>
    /// <param name="tileY">Preferred tile y.</param>
    /// <returns>The start.</returns>
    public static MapStart NearTile(int tileX, int tileY) => new() { TileX = tileX, TileY = tileY };

    /// <summary>Starts where the server put the player.</summary>
    /// <param name="entry">The server's answer.</param>
    /// <returns>The start.</returns>
    public static MapStart At(MapEntry entry) => new() { Exact = true, X = entry.X, Y = entry.Y, Facing = entry.Facing };
}
