// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Core.Interpolation;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Shops;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.Socket.Calendar;
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
    private const int MenuUnitsTall = 204;
    private const int NarrowCanvasWidth = 640;

    /// <summary>Seconds the screen takes to go black when the server changes the map.</summary>
    public const double FadeOutSeconds = 0.2;

    /// <summary>Seconds the new map takes to appear.</summary>
    public const double FadeInSeconds = 0.25;

    // The map's own animations cover lily pads and grass, not the pond surface: ripple water tiles.
    private static readonly TileWave s_waterWave = new(TileTraits.Water, StripHeight: 4, Amplitude: 1, Wavelength: 24, PeriodMs: 1600);

    private readonly MapLoader _loader;
    private readonly FarmerTextures _farmerTextures;
    private readonly RenderCommandBuffer _commands = new(8192);
    private readonly WorldSpriteBatch _world = new(1024);
    private readonly PlayerChannel? _channel;
    private readonly RemotePlayers? _remote;
    private readonly PlayerActions? _actions;
    private readonly LookChannel? _looks;
    private readonly NicknameLabels? _labels;
    private readonly int[] _labelIds = new int[RemotePlayers.MaxPlayers + 1];
    private readonly int[] _labelX = new int[RemotePlayers.MaxPlayers + 1];
    private readonly int[] _labelY = new int[RemotePlayers.MaxPlayers + 1];
    private int _labelCount;
    private readonly PlaceableChannel? _placed;
    private readonly SkyLayer _sky;
    private readonly PlacementActions? _placement;
    private readonly PlacedSet _placedSet = new();
    private readonly NodeChannel? _nodes;
    private WalkGrid? _nodeGrid;
    private MapContent? _nodeGridContent;
    private int _nodeGridVersion = -1;
    private WalkGrid? _joinedGrid;
    private WalkGrid? _joinedNodes;
    private WalkGrid? _joinedPlaced;
    private WalkGrid? _placedGrid;
    private MapContent? _placedGridContent;
    private int _placedGridVersion = -1;
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
    /// <param name="actions">Number keys, the action key and the panel keys; <see langword="null"/> when there is no server to act on.</param>
    /// <param name="placed">The mirror of the pieces placed in the player's place; they block walking like the server's grid does.</param>
    /// <param name="placement">The keys that place, turn, move and pick up furniture.</param>
    /// <param name="looks">The looks the server sent for the players on the map; <see langword="null"/> draws everyone with <paramref name="look"/>.</param>
    /// <param name="labels">Nickname labels over players' heads; <see langword="null"/> draws no names.</param>
    /// <param name="nodes">The mirror of the resource nodes of the player's place; the ones that block walk like the server's grid does.</param>
    /// <param name="calendar">The server calendar: it gives the time of day and the weather for the sky (docs/adr/ADR-027); <see langword="null"/> keeps plain daylight.</param>
    public MapScene(MapLoader loader, MapContent content, FarmerTextures farmerTextures, FarmerLook look, MapStart start, PlayerChannel? channel = null, PlayerActions? actions = null, PlaceableChannel? placed = null, PlacementActions? placement = null, LookChannel? looks = null, NicknameLabels? labels = null, NodeChannel? nodes = null, CalendarChannel? calendar = null)
    {
        _sky = new SkyLayer(calendar, placed);
        _actions = actions;
        _nodes = nodes;
        _placed = placed;
        _placement = placement;
        _looks = looks;
        _labels = labels;
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
        WalkGrid grid = this.GridOf(content);
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
                _farmer.Step(input, StepSeconds, grid);
            }

            _walkTime = _farmer.IsMoving ? _walkTime + (steps * StepSeconds) : 0;
            _channel?.Report((float)_farmer.X, (float)_farmer.Y, (byte)_farmer.Facing, _farmer.IsMoving);
        }

        // Keys act on the map shown, and not while a map change freezes the farmer.
        InputEdges liveEdges = _phase == Phase.FadingOut ? InputEdges.None : edges;
        int shopId = 0;
        if (content.Spec.Shops.Count > 0)
        {
            _ = ShopRules.TryFindShop(content.Spec, _farmer.X, _farmer.Y, map.TileWidth, out shopId);
        }

        bool elevatorNear = content.Spec.Elevator is { } elevator
            && ShopRules.InReach(new ShopPoint(0, elevator.TileX, elevator.TileY), _farmer.X, _farmer.Y, map.TileWidth);

        // Tools work wherever the ground can be farmed or nothing can be placed; in a room the action key places furniture instead.
        // The rotate key turns a piece being carried and is not a wide swing then.
        bool placeableMap = content.Spec.Placeable;
        InputEdges toolEdges = _placement is { CarryingId: not 0 } ? liveEdges & ~InputEdges.Rotate : liveEdges;
        BuildingDefinition? interior = content.Spec.IsInterior && BuildingCatalog.TryGet(content.Spec.InteriorOf, out BuildingDefinition? building) ? building : null;
        _actions?.Update(toolEdges, _farmer.X, _farmer.Y, _farmer.Facing, map.TileWidth, content.Spec.Farmable || !placeableMap, _clockSeconds, shopId, placeableMap ? _placedSet : null, elevatorNear, interior);
        if (_actions is not null)
        {
            // The action key that opened a chest or a furnace is not also a placement.
            InputEdges placementEdges = _actions.ActionConsumed ? liveEdges & ~InputEdges.Action : liveEdges;
            _placement?.Update(placeableMap && _phase == Phase.Playing, placementEdges, _actions.TargetX, _actions.TargetY, _farmer.Facing, _placedSet, content.Grid, _farmer.X, _farmer.Y, _clockSeconds, !content.Spec.Farmable);
        }

        int scale = ScaleFor(map, canvasWidth, canvasHeight);
        _camera.Follow(
            (int)_farmer.X,
            (int)_farmer.Y - (FarmerSprite.Height / 2),
            (canvasWidth + scale - 1) / scale,
            (canvasHeight + scale - 1) / scale,
            map.Width * map.TileWidth,
            map.Height * map.TileHeight,
            HudBottomInset(canvasWidth, canvasHeight) / scale);

        _commands.Begin(scale);
        foreach (TileLayer layer in content.Layers.Ground)
        {
            TileLayerRenderer.Draw(map, layer, content.TileSetTextures, _camera, _commands, timeMs, s_waterWave);
        }

        IMapDecorator[] decorators = content.Decorators;
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].SetView(_camera);
            decorators[i].DrawOnGround(_commands, _camera, timeMs);
        }

        int walkFrame = _farmer.IsMoving ? 1 + (int)(_walkTime / WalkFrameSeconds) : 0;
        _world.Begin();
        for (int i = 0; i < decorators.Length; i++)
        {
            decorators[i].AddToWorld(_world);
        }

        _labelCount = 0;
        LookInfo ownLook = default;
        bool hasOwn = _looks is not null && _looks.TryGetLocal(out ownLook);
        FarmerSprite.Draw(_world, _farmerTextures, hasOwn ? LookSprites.Resolve(ownLook) : this.Look, _farmer.Facing, walkFrame, (int)_farmer.X, (int)_farmer.Y);
        this.AddLabel(ownLook.Nickname, (int)_farmer.X, (int)_farmer.Y);
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

        // The sky: the night tint with the lamps cut out of it over the whole world, then the rain or snow, then the names and the fade.
        _sky.DrawAmbient(_commands, content.Spec, _camera, map.TileWidth);
        _sky.Update(content.Spec, frameSeconds, (canvasWidth + scale - 1) / scale, (canvasHeight + scale - 1) / scale);
        if (!ViewOptions.ReduceMotion)
        {
            _sky.DrawPrecipitation(_commands);
        }
        this.DrawLabels();
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

    // The grid the farmer walks on: the map's own, plus the footprints of the pieces the server says are placed here. Rebuilt only
    // when the mirror or the map changed (never per frame), with the same PlacedSet rule the server uses.
    private WalkGrid GridOf(MapContent content)
    {
        WalkGrid nodes = this.NodeGridOf(content);
        if (_placed is null || !content.Spec.Placeable)
        {
            return nodes;
        }

        int version = _placed.Version;
        if (version != _placedGridVersion || !ReferenceEquals(content, _placedGridContent) || _placedGrid is null)
        {
            _placedSet.Clear();
            using (_placed.Read())
            {
                if (string.Equals(_placed.MapId, content.Spec.Id, StringComparison.Ordinal))
                {
                    for (int i = 0; i < _placed.Count; i++)
                    {
                        PlacedObjectInfo piece = _placed.At(i);
                        _placedSet.Add(new PlacedItem(piece.Id, piece.ItemId, piece.TileX, piece.TileY, piece.Rotation));
                    }
                }
            }

            _placedGrid = _placedSet.BuildGrid(content.Grid);
            _placedGridContent = content;
            _placedGridVersion = version;
        }

        // The farm has both layers: the grid is the nodes' joined with the placed footprints (the server's InstanceWorld does the same). Built again only when either layer replaced its grid.
        if (ReferenceEquals(nodes, content.Grid))
        {
            return _placedGrid;
        }

        if (_joinedGrid is null || !ReferenceEquals(_joinedNodes, nodes) || !ReferenceEquals(_joinedPlaced, _placedGrid))
        {
            WalkGrid joined = nodes.Clone();
            joined.BlockWhereBlocked(_placedGrid);
            _joinedGrid = joined;
            _joinedNodes = nodes;
            _joinedPlaced = _placedGrid;
        }

        return _joinedGrid;
    }

    // The map's own grid plus the tiles of the nodes that block walking (docs/adr/ADR-021 D3), the same rule the server builds with NodeField.
    // Rebuilt only when the mirror or the map changed.
    private WalkGrid NodeGridOf(MapContent content)
    {
        if (_nodes is null)
        {
            return content.Grid;
        }

        int version = _nodes.Version;
        if (version != _nodeGridVersion || !ReferenceEquals(content, _nodeGridContent) || _nodeGrid is null)
        {
            NodeField field = new();
            using (_nodes.Read())
            {
                if (string.Equals(_nodes.MapId, content.Spec.Id, StringComparison.Ordinal))
                {
                    for (int i = 0; i < _nodes.Count; i++)
                    {
                        NodeInfo node = _nodes.At(i);
                        field.Add(new ResourceNode(node.Id, node.X, node.Y, (NodeKind)node.Kind, node.Variant, node.Stage, node.Hits));
                    }
                }
            }

            _nodeGrid = field.BuildGrid(content.Grid);
            _nodeGridContent = content;
            _nodeGridVersion = version;
        }

        return _nodeGrid;
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
            LookInfo known = default;
            FarmerLook look = _looks is not null && _looks.TryGet(id, out known) ? LookSprites.Resolve(known) : this.Look;
            FarmerSprite.Draw(_world, _farmerTextures, look, (Facing)(pose.State & 3), walk, (int)pose.X, (int)pose.Y, (int)id);
            this.AddLabel(known.Nickname, (int)pose.X, (int)pose.Y);
        }
    }

    // Remembers where a nickname goes; the labels are recorded after everything else so no sprite or roof covers them.
    private void AddLabel(string? nickname, int feetX, int feetY)
    {
        if (!ViewOptions.ShowNames)
        {
            return;
        }

        int id = _labels?.IdOf(nickname) ?? 0;
        if (id > 0 && _labelCount < _labelIds.Length)
        {
            _labelIds[_labelCount] = id;
            _labelX[_labelCount] = feetX;
            _labelY[_labelCount] = feetY;
            _labelCount++;
        }
    }

    // One label command per player: the bitmap was uploaded when the name arrived, so the frame still crosses to the browser once.
    private void DrawLabels()
    {
        for (int i = 0; i < _labelCount; i++)
        {
            _commands.DrawLabel(_labelIds[i], _labelX[i] - _camera.Left, _labelY[i] - FarmerSprite.Height - _camera.Top - 1);
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
