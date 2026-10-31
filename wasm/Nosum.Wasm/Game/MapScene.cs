// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Shops;
using Nosum.Gameplay.Weather;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Minimap;

namespace Nosum.Wasm.Game;

/// <summary>
/// The playable world: one map at a time with one local farmer. Fixed-step movement, a camera that follows, and the
/// layer order of docs/adr/ADR-005 (ground, depth-sorted world, front). Any map of <see cref="GameMaps"/> works: the scene
/// draws its layers and whatever its <see cref="IMapDecorator"/>s add (buildings now; crops, soil later). With a
/// <see cref="PlayerChannel"/> the local farmer is predicted here and checked by the server, the other farmers on the map
/// are interpolated and depth-sorted with it (docs/adr/ADR-008), and the server changes the map: the scene fades out, waits
/// (without blocking the frame) until <see cref="MapLoader"/> has the new map, swaps, and fades in (docs/adr/ADR-012).
/// </summary>
internal sealed partial class MapScene
{
    private const double StepSeconds = 1.0 / 60.0;
    private const double MaxFrameSeconds = 0.25;
    private const double WalkFrameSeconds = 0.085;
    private const int TargetTilesAcross = 26;
    private const int MaxScale = 6;
    private const int MenuUnitsTall = 204;
    private const int NarrowCanvasWidth = 640;

    // The walk animation plays this many times faster while running (docs/adr/ADR-081 D4).
    private const double RunAnimationSpeed = 1.6;

    /// <summary>Seconds the screen takes to go black when the server changes the map.</summary>
    public const double FadeOutSeconds = 0.2;

    /// <summary>Seconds the new map takes to appear.</summary>
    public const double FadeInSeconds = 0.25;

    // The map's own animations cover lily pads and grass, not the pond surface: ripple water tiles.
    private static readonly TileWave s_waterWave = new(TileTraits.Water, StripHeight: 4, Amplitude: 1, Wavelength: 24, PeriodMs: 1600);

    private readonly MapLoader _loader;
    private readonly FarmerTextures _farmerTextures;
    private readonly RenderCommandBuffer _commands = new(8192);
    private readonly WorldSpriteBatch _world = new(4096);
    private readonly PlayerChannel? _channel;
    private readonly RemotePlayers? _remote;
    private readonly PlayerActions? _actions;
    private readonly LookChannel? _looks;
    private readonly NicknameLabels? _labels;
    private readonly CombatLabels? _combatLabels;
    private readonly int[] _labelIds = new int[RemotePlayers.MaxPlayers + 1];
    private readonly int[] _labelX = new int[RemotePlayers.MaxPlayers + 1];
    private readonly int[] _labelY = new int[RemotePlayers.MaxPlayers + 1];
    private int _labelCount;
    private readonly SkyLayer _sky;
    private readonly GameAudio? _audio;
    private readonly PlacementActions? _placement;
    private readonly CompositeWalkGrid _compositeGrid;
    private readonly CalendarChannel? _calendar;
    private readonly ProgressChannel? _progress;
    private readonly FarmerFeed? _feed;
    private readonly CombatChannel? _combat;
    private readonly MinimapViewModel? _minimapFrame;
    private FixedTimestep _clock = new(StepSeconds, 0.25);
    private MapContent _content;
    private Walker _farmer;
    private Camera2D _camera;
    private Phase _phase;
    private double _fade;
    private MapEntry _pending;
    private string? _waitingMapId;
    private uint _ignoreCorrectionsBefore;
    private double _walkTime;
    private double _clockSeconds;
    private int _pointerX;
    private int _pointerY;
    private bool _pointerPending;
    private int _dragX;
    private int _dragY;
    private bool _dragHeld;

    /// <summary>Initializes the scene.</summary>
    /// <param name="loader">Loads the maps the server sends the player to.</param>
    /// <param name="content">The map to start on.</param>
    /// <param name="farmerTextures">Farmer sheet texture ids.</param>
    /// <param name="look">The farmer's look.</param>
    /// <param name="start">Where to start: the preferred tile, or the place the server chose.</param>
    /// <param name="channel">Server connection for multiplayer, or <see langword="null"/> to play offline (no map changes).</param>
    /// <param name="actions">Number keys, the action key and the panel keys; <see langword="null"/> when there is no server to act on.</param>
    /// <param name="placed">The mirror of the pieces placed in the player's place; they block walking like the server's grid does.</param>
    /// <param name="placement">The keys that place, turn, move and pick up furniture.</param>
    /// <param name="looks">The looks the server sent for the players on the map; <see langword="null"/> draws everyone with <paramref name="look"/>.</param>
    /// <param name="labels">Nickname labels over players' heads; <see langword="null"/> draws no names.</param>
    /// <param name="nodes">The mirror of the resource nodes of the player's place; the ones that block walk like the server's grid does.</param>
    /// <param name="calendar">The server calendar: it gives the time of day and the weather for the sky (docs/adr/ADR-027); <see langword="null"/> keeps plain daylight.</param>
    /// <param name="audio">The audio director: sounds queued by the hooks are written into the frame, and the footsteps, music and rain loop are decided here (docs/adr/ADR-039); <see langword="null"/> plays no sound.</param>
    /// <param name="combatLabels">Pre-interned combat numbers, used as the energy labels that float above a tool's tile (docs/adr/ADR-071 D4); <see langword="null"/> shows none.</param>
    /// <param name="feed">The farmers of each frame, for the decorators that follow them (pets, emote bubbles); <see langword="null"/> keeps the feed empty.</param>
    /// <param name="progress">The player's energy, which gates running (docs/adr/ADR-081 D3); <see langword="null"/> means no energy is known and the farmer does not run.</param>
    /// <param name="combat">The combat channel whose impacts shake the view (docs/adr/ADR-076); <see langword="null"/> never shakes.</param>
    /// <param name="minimap">The minimap frame in the HUD, told whenever the canvas shows or hides the map; <see langword="null"/> has no frame.</param>
    public MapScene(MapLoader loader, MapContent content, FarmerTextures farmerTextures, FarmerLook look, MapStart start, PlayerChannel? channel = null, PlayerActions? actions = null, PlaceableChannel? placed = null, PlacementActions? placement = null, LookChannel? looks = null, NicknameLabels? labels = null, NodeChannel? nodes = null, CalendarChannel? calendar = null, GameAudio? audio = null, CombatLabels? combatLabels = null, CombatChannel? combat = null, MinimapViewModel? minimap = null, FarmerFeed? feed = null, ProgressChannel? progress = null)
    {
        _sky = new SkyLayer(calendar, placed);
        _calendar = calendar;
        _combat = combat;
        _minimapFrame = minimap;
        _feed = feed;
        _progress = progress;

        _audio = audio;
        _actions = actions;
        _placement = placement;
        _compositeGrid = new CompositeWalkGrid(nodes, placed);
        _looks = looks;
        _labels = labels;
        _combatLabels = combatLabels;
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

    /// <summary>Gets or sets what a farmer wears until the server's look for them arrives.</summary>
    public FarmerLook Look { get; set; }

    /// <summary>Gets the id of the map being shown (the old one until a map change has faded out).</summary>
    public string MapId => _content.Spec.Id;

    /// <summary>
    /// Raised when the screen is black and waits for a map the server sent the player to: the map's id, then <see langword="null"/> once the map is shown.
    /// Raised on the frame thread, only when the wait starts or ends.
    /// </summary>
    public event Action<string?>? WaitingChanged;

    /// <summary>Remembers a click or tap on the canvas; the next <see cref="Frame"/> checks whether it hit a merchant. Allocation-free.</summary>
    /// <param name="x">Click x, canvas CSS pixels.</param>
    /// <param name="y">Click y, canvas CSS pixels.</param>
    public void Pointer(int x, int y)
    {
        _pointerX = x;
        _pointerY = y;
        _pointerPending = true;
    }

    /// <summary>Tells the scene where the mouse is and whether a drag is on; the next <see cref="Frame"/> works the tiles it crosses (drag-to-sow). Allocation-free.</summary>
    /// <param name="x">Mouse x, canvas CSS pixels.</param>
    /// <param name="y">Mouse y, canvas CSS pixels.</param>
    /// <param name="held">Whether the drag is on: the mouse button is down over the world.</param>
    public void SetPointer(int x, int y, bool held)
    {
        _dragX = x;
        _dragY = y;
        _dragHeld = held;
    }

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
        WalkGrid grid = _compositeGrid.GridOf(content);
        _clockSeconds += frameSeconds;
        long timeMs = (long)(_clockSeconds * 1000);
        long localMs = Environment.TickCount64;
        long serverMs = _calendar is { IsLoaded: true } ? _calendar.ServerNowMs() : localMs;
        FrameClock frameClock = new(localMs, serverMs, _clockSeconds);

        if (_phase == Phase.FadingOut)
        {
            // Frozen while the server's new map loads: no steps, no reports, the old map still on screen.
            _clock.Advance(frameSeconds);
        }
        else
        {
            _clock.Advance(frameSeconds);
            bool swinging = _actions is not null && _actions.SwingLocked(_clockSeconds);
            // Running (docs/adr/ADR-081 D3): run is held, the farmer is walking and there is energy to spend. The server grants the same speed and
            // withdraws it when the energy runs out, and the walk animation plays faster meanwhile.
            bool walking = !swinging && (input & (InputState.Up | InputState.Down | InputState.Left | InputState.Right)) != 0;
            bool running = walking && (input & InputState.Run) != 0 && _progress is { Energy: > 0 };
            double speed = RunRules.SpeedMultiplier(_actions?.MoveSpeedFactor ?? 1, running);
            double remaining = Math.Clamp(frameSeconds, 0, MaxFrameSeconds);
            do
            {
                double dt = Math.Min(remaining, StepSeconds);
                // A swing holds the farmer in place until it is over (docs/adr/ADR-071 D1).
                _farmer.Step(swinging ? default : input, dt, grid, speed);
                remaining -= dt;
            }
            while (remaining > 1e-9);

            _walkTime = _farmer.IsMoving ? _walkTime + (frameSeconds * (running ? RunAnimationSpeed : 1)) : 0;
            _channel?.Report((float)_farmer.X, (float)_farmer.Y, (byte)_farmer.Facing, _farmer.IsMoving, running);
        }

        // Keys act on the map shown, and not while a map change freezes the farmer.
        InputEdges liveEdges = _phase == Phase.FadingOut ? InputEdges.None : edges;
        if (_phase == Phase.FadingOut)
        {
            // A swing queued before the map changed must not land on the new map's tiles.
            _actions?.CancelQueuedUse();
        }

        int shopId = 0;
        if (content.Spec.Shops.Count > 0)
        {
            _ = ShopRules.TryFindShop(content.Spec, _farmer.X, _farmer.Y, map.TileWidth, out shopId);
        }

        if (_pointerPending)
        {
            _pointerPending = false;
            if (_phase != Phase.FadingOut)
            {
                this.TalkToMerchantAt(content.Spec, map.TileWidth, ScaleFor(map, canvasWidth, canvasHeight));
                this.CastAtPointer(content.Spec, map.TileWidth, ScaleFor(map, canvasWidth, canvasHeight));
            }
        }

        bool elevatorNear = (content.Spec.Elevator is { } elevator
                && ShopRules.InReach(new ShopPoint(0, elevator.TileX, elevator.TileY), _farmer.X, _farmer.Y, map.TileWidth))
            || (content.Spec.MineFloor >= 1
                && ShopRules.InReach(new ShopPoint(0, content.Spec.SpawnTileX, content.Spec.SpawnTileY), _farmer.X, _farmer.Y, map.TileWidth));

        // Tools work wherever the ground can be farmed or nothing can be placed; in a room the action key places furniture instead.
        // While carrying a piece, the rotate key turns it and the action key sets it down rather than swinging a tool or opening a container.
        bool placeableMap = content.Spec.Placeable;
        InputEdges toolEdges = _placement is { CarryingId: not 0 } ? liveEdges & ~(InputEdges.Rotate | InputEdges.Action) : liveEdges;
        BuildingDefinition? interior = content.Spec.IsInterior && BuildingCatalog.TryGet(content.Spec.InteriorOf, out BuildingDefinition? building) ? building : null;
        _actions?.Update(toolEdges, _farmer.X, _farmer.Y, _farmer.Facing, map.TileWidth, content.Spec.Farmable || !placeableMap, _clockSeconds, shopId, placeableMap ? _compositeGrid.PlacedSet : null, elevatorNear, interior, grid);
        if (_actions is not null)
        {
            // The action key that opened a chest or a furnace is not also a placement.
            InputEdges placementEdges = _actions.ActionConsumed ? liveEdges & ~InputEdges.Action : liveEdges;
            _placement?.Update(placeableMap && _phase == Phase.Playing, placementEdges, _actions.TargetX, _actions.TargetY, _farmer.Facing, _compositeGrid.PlacedSet, content.Grid, _farmer.X, _farmer.Y, _clockSeconds, !content.Spec.Farmable, content);
        }

        int scale = ScaleFor(map, canvasWidth, canvasHeight);
        int farmerPx = (int)Math.Round(_farmer.X);
        int farmerPy = (int)Math.Round(_farmer.Y);
        _camera.Follow(
            farmerPx,
            farmerPy - (FarmerSprite.Height / 2),
            (canvasWidth + scale - 1) / scale,
            (canvasHeight + scale - 1) / scale,
            map.Width * map.TileWidth,
            map.Height * map.TileHeight,
            HudBottomInset(canvasWidth, canvasHeight) / scale);

        // A crit or a finisher shakes the view for a moment (docs/adr/ADR-076); reduced motion keeps it still.
        if (!ViewOptions.ReduceMotion && _combat is not null && _combat.TryGetActiveShake(out int shakeAmplitude, out int shakeElapsedMs, out int shakeDurationMs))
        {
            CameraShake.Offset(shakeElapsedMs, shakeDurationMs, shakeAmplitude, out int shakeX, out int shakeY);
            _camera.Nudge(shakeX, shakeY);
        }

        // Drag-to-sow: the tile under the held mouse, in this frame's camera. The server checks each tile the drag reaches.
        if (_actions is not null && _phase == Phase.Playing)
        {
            double dragWorldX = _camera.Left + ((double)_dragX / scale);
            double dragWorldY = _camera.Top + ((double)_dragY / scale);
            _actions.DragSow(_farmer.X, _farmer.Y, map.TileWidth, (int)Math.Floor(dragWorldX / map.TileWidth), (int)Math.Floor(dragWorldY / map.TileWidth), _dragHeld, content.Spec.Farmable || !placeableMap, _clockSeconds);
        }

        if (_audio is not null)
        {
            bool hasSky = _sky.TryGetState(out Season season, out int minuteOfDay, out WeatherKind weather);
            _audio.Frame(frameSeconds, farmerPx, farmerPy, _phase != Phase.FadingOut && _farmer.IsMoving, content.Spec.Indoors, hasSky, season, minuteOfDay, weather, content.Spec.MineFloor);
        }

        // A mine floor's cave tiles take its biome's palette (docs/adr/ADR-070 D2); every other map is drawn unchanged.
        int biomeTint = MineBiomeLook.TintForFloor(content.Spec.MineFloor);
        _commands.Begin(scale);
        _audio?.Flush(_commands);
        foreach (TileLayer layer in content.Layers.Ground)
        {
            TileLayerRenderer.Draw(map, layer, content.TileSetTextures, _camera, _commands, timeMs, s_waterWave, biomeTint);
        }

        IMapDecorator[] decorators = content.Decorators;
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].SetView(_camera);
            decorators[i].SetClock(frameClock);
            decorators[i].SetFarmer(farmerPx, farmerPy);
            decorators[i].DrawOnGround(_commands, _camera, timeMs);
        }

        int walkFrame = _farmer.IsMoving ? 1 + (int)(_walkTime / WalkFrameSeconds) : 0;
        this.CollectFarmers();
        _world.Begin();
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].AddToWorld(_world);
        }

        _labelCount = 0;
        LookInfo ownLook = default;
        bool hasOwn = _looks is not null && _looks.TryGetLocal(out ownLook);
        int heldItemId = _actions?.HeldItemId ?? 0;
        float swingProgress = -1f;
        if (_actions is not null && _actions.TryGetActiveSwing(_clockSeconds, out float activeSwing, out int swungItem))
        {
            swingProgress = activeSwing;
            if (swungItem > 0)
            {
                heldItemId = swungItem;
            }
        }

        Facing effectiveFacing = _farmer.Facing;
        byte activeSkillId = 0;
        float skillProgress = 0f;
        bool hasSkillEffect = _actions is not null && _actions.TryGetActiveSkillEffect(localMs, out activeSkillId, out skillProgress, out _, out _);
        if (hasSkillEffect && activeSkillId == 1)
        {
            if (effectiveFacing == Facing.Up)
            {
                effectiveFacing = Facing.Down;
            }

            if (swingProgress < 0f)
            {
                swingProgress = skillProgress;
            }
        }

        FarmerSprite.Draw(_world, _farmerTextures, hasOwn ? LookSprites.Resolve(ownLook) : this.Look, effectiveFacing, walkFrame, farmerPx, farmerPy, 0, heldItemId, swingProgress, _actions?.Exhausted ?? false);
        this.AddEnergyFloats();
        if (hasSkillEffect)
        {
            FarmerSprite.DrawSkillEffect(_world, _farmerTextures, activeSkillId, skillProgress, farmerPx, farmerPy);
        }

        this.AddLabel(ownLook.Nickname, farmerPx, farmerPy);
        if (_phase != Phase.FadingOut)
        {
            this.AddRemoteFarmers();
        }

        _world.Flush(_commands, _camera, deferLabels: true);
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].DrawInFront(_commands, _camera);
        }

        foreach (TileLayer layer in content.Layers.Front)
        {
            TileLayerRenderer.Draw(map, layer, content.TileSetTextures, _camera, _commands, timeMs, s_waterWave, biomeTint);
        }

        // The sky: the night tint with the lamps cut out of it over the whole world, then the rain or snow, then the names and the fade.
        _sky.DrawAmbient(_commands, content.Spec, _camera, map.TileWidth);
        _sky.Update(content.Spec, frameSeconds, (canvasWidth + scale - 1) / scale, (canvasHeight + scale - 1) / scale);
        if (!ViewOptions.ReduceMotion)
        {
            _sky.DrawPrecipitation(_commands);
        }
        _sky.DrawLightning(_commands, _audio, (canvasWidth + scale - 1) / scale, (canvasHeight + scale - 1) / scale);
        EatFeedback.Draw(_commands, farmerPx - _camera.Left, farmerPy - (FarmerSprite.Height / 2) - _camera.Top, timeMs);
        _world.FlushLabels(_commands, _camera);
        this.DrawLabels();

        // The minimap sits in the HUD corner, above the world and the labels, and below the fade.
        this.DrawMinimap(content, edges, canvasWidth, canvasHeight, scale);
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

        int auto = ViewZoom.Pick(map.Width * map.TileWidth, map.Height * map.TileHeight, map.TileWidth, canvasWidth, canvasHeight, HudBottomInset(canvasWidth, canvasHeight), TargetTilesAcross, MaxScale);
        int chosen = ViewOptions.Scale;
        if (chosen < 2)
        {
            return auto;
        }

        // The zoom the player picked (docs/adr/ADR-033 D1); a room is still shown whole, so it never goes past what fits.
        return map.Width * map.TileWidth > TargetTilesAcross * map.TileWidth ? chosen : Math.Min(chosen, auto);
    }

    /// <summary>
    /// The strip at the bottom of the canvas the hotbar covers: 40 UI pixels (the hotbar and the screen gutter). The UI pixel is the one
    /// <c>nosum-theme.js</c> picks (<c>unitFor</c>: the open-map zoom, lowered so the menu fits the height).
    /// </summary>
    /// <param name="canvasWidth">Canvas width, CSS pixels.</param>
    /// <param name="canvasHeight">Canvas height, CSS pixels.</param>
    /// <returns>The strip's height, CSS pixels.</returns>
    internal static int HudBottomInset(int canvasWidth, int canvasHeight)
    {
        int chosen = ViewOptions.Scale;
        int world = chosen >= 2 ? chosen : Math.Clamp((int)Math.Round(canvasWidth / (double)(TargetTilesAcross * 16)), 2, MaxScale);
        int fits = Math.Max(2, canvasHeight / MenuUnitsTall);

        // Narrow screens show the hotbar in two rows (Hotbar.razor.css, max-width 640px), so the strip is taller.
        return (canvasWidth <= NarrowCanvasWidth ? 64 : 40) * Math.Min(world, fits);
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
