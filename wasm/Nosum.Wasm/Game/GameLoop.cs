// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Chest;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Coop;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.Socket.GroundItem;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.Socket.Player;
using Nosum.Rendering;
using Nosum.Rendering.Web;
using Nosum.Wasm.ViewModels.Maps;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.Game;

/// <summary>
/// Loads the map the server put the player on, then runs the frame loop: JS calls <see cref="Frame"/> once per
/// animation frame and the scene submits one draw list back (docs/adr/ADR-002). Later maps are loaded by the
/// scene's <see cref="MapLoader"/> in the background when the server changes the player's map.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class GameLoop
{
    private const string FarmerUrl = "assets/farmer/";

    private static readonly TimeSpan s_entryTimeout = TimeSpan.FromSeconds(10);

    private static readonly WebRenderBackend s_backend = new();
    private static MapScene? s_scene;
    private static PlayerChannel? s_network;
    private static CalendarChannel? s_calendar;
    private static FarmChannel? s_farm;
    private static InventoryChannel? s_inventory;
    private static PlaceableChannel? s_placeable;
    private static NodeChannel? s_nodes;
    private static GroundItemChannel? s_groundItems;
    private static PlacementViewModel? s_placement;
    private static ChestChannel? s_chests;
    private static FurnaceChannel? s_furnaces;
    private static BuildingChannel? s_buildings;
    private static CoopChannel? s_coop;
    private static MarketChannel? s_market;
    private static ProgressChannel? s_progress;
    private static CombatChannel? s_combat;
    private static IStringLocalizer<Language>? s_text;
    private static LookChannel? s_looks;
    private static NicknameFormatter? s_names;
    private static GameAudio? s_audio;
    private static MapLoadingViewModel? s_loading;
    private static string? s_waitingMapId;
    private static double s_lastTime = -1;
    private static bool s_moved;
    private static bool s_acted;
    private static volatile bool s_modalOpen;
    private static volatile bool s_overlayOpen;
    private static volatile bool s_connectionBlocked;

    private const InputEdges PanelKeysMask =
        InputEdges.Inventory | InputEdges.Wardrobe | InputEdges.Trade |
        InputEdges.Coop | InputEdges.Help | InputEdges.Close | InputEdges.Profile;

    /// <summary>Updates whether a modal panel, NPC conversation, or blocking overlay dialog is open on screen.</summary>
    /// <param name="modalOpen">Whether any standalone panel, NPC dialog box, or overlay dialog is open.</param>
    /// <param name="overlayOpen">Whether a top-level overlay dialog (such as the elevator picker or account form) is open.</param>
    public static void SetModalState(bool modalOpen, bool overlayOpen)
    {
        s_modalOpen = modalOpen;
        s_overlayOpen = overlayOpen;
    }

    /// <summary>Holds back all game input while the server connection is not ready; the reconnect overlay covers the game meanwhile.</summary>
    /// <param name="blocked">Whether the connection is down, so no input reaches the game.</param>
    public static void SetConnectionBlocked(bool blocked) => s_connectionBlocked = blocked;

    /// <summary>Connects the game to the server; call before <see cref="StartAsync"/>. Without it the farm is played offline.</summary>
    /// <param name="channel">The player channel.</param>
    public static void UseNetwork(PlayerChannel channel) => s_network = channel;

    /// <summary>Connects farming to the game: the farm mirror and the held item. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="farm">The farm mirror and action sender.</param>
    /// <param name="inventory">The inventory mirror.</param>
    /// <param name="market">Ships crops at the bin.</param>
    public static void UseFarming(FarmChannel farm, InventoryChannel inventory, MarketChannel market)
    {
        s_farm = farm;
        s_inventory = inventory;
        s_market = market;
    }

    /// <summary>Connects energy and skills to the game: with it the action key eats the food in hand while the player is not full of energy (docs/adr/ADR-023). Call before <see cref="StartAsync"/>.</summary>
    /// <param name="progress">The energy mirror and eat sender.</param>
    public static void UseProgress(ProgressChannel progress) => s_progress = progress;

    /// <summary>Connects MMORPG combat and monsters to the game (docs/adr/ADR-045). Call before <see cref="StartAsync"/>.</summary>
    /// <param name="combat">The combat channel.</param>
    /// <param name="text">Localized UI strings for world combat labels.</param>
    public static void UseCombat(CombatChannel combat, IStringLocalizer<Language>? text = null)
    {
        s_combat = combat;
        s_text = text;
    }

    /// <summary>Connects the resource nodes to the game: the mirror that is drawn and that blocks walking. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="nodes">The mirror of the nodes of the player's place.</param>
    public static void UseNodes(NodeChannel nodes) => s_nodes = nodes;

    /// <summary>Connects the server calendar, so crops that withered at a season change are not drawn; call before <see cref="StartAsync"/>.</summary>
    /// <param name="calendar">The server calendar.</param>
    public static void UseCalendar(CalendarChannel calendar) => s_calendar = calendar;

    /// <summary>Connects the looks the server sent to the farmers drawn; call before <see cref="StartAsync"/>.</summary>
    /// <param name="looks">The mirror of the looks on the map.</param>
    /// <param name="names">Formats the nicknames drawn above the farmers.</param>
    public static void UseLooks(LookChannel looks, NicknameFormatter names)
    {
        s_looks = looks;
        s_names = names;
    }

    /// <summary>Connects furniture placing to the game: the placed pieces and the arrange panel's state. Call before <see cref="StartAsync"/>, after <see cref="UseFarming"/>.</summary>
    /// <param name="placeable">The placed pieces mirror and action sender.</param>
    /// <param name="placement">The arrange panel's view model.</param>
    public static void UsePlacement(PlaceableChannel placeable, PlacementViewModel placement)
    {
        s_placeable = placeable;
        s_placement = placement;
    }

    /// <summary>Connects chests and furnaces to the game: the action key facing one opens it, and a working furnace is drawn lit. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="chests">The open chest's mirror and request sender.</param>
    /// <param name="furnaces">The furnaces' mirror.</param>
    public static void UseContainers(ChestChannel chests, FurnaceChannel furnaces)
    {
        s_chests = chests;
        s_furnaces = furnaces;
    }

    /// <summary>Connects the items lying on the ground to the game: they are drawn as world sprites. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="groundItems">The ground items mirror.</param>
    public static void UseGroundItems(GroundItemChannel groundItems) => s_groundItems = groundItems;

    /// <summary>Gives the loop the co-op state, so co-op land the co-op has not unlocked is shaded (docs/adr/ADR-038). Call before <see cref="StartAsync"/>.</summary>
    /// <param name="coop">The co-op channel.</param>
    public static void UseCoop(CoopChannel coop) => s_coop = coop;

    /// <summary>Gives the loop the farm buildings and their animals (docs/adr/ADR-028): buildings under construction are drawn dull, animals inside a building are drawn and tended with the action key.</summary>
    /// <param name="buildings">The building channel.</param>
    public static void UseBuildings(BuildingChannel buildings) => s_buildings = buildings;

    /// <summary>Gives the loop the audio director (docs/adr/ADR-039): it loads the sound files in the background and its queue is written into every frame. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="audio">The audio director.</param>
    internal static void UseAudio(GameAudio audio) => s_audio = audio;

    /// <summary>Gives the loop the overlay that shows a map's progress while the screen is black and the map loads. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="loading">The loading overlay's view model.</param>
    public static void UseMapLoading(MapLoadingViewModel loading) => s_loading = loading;

    /// <summary>Tells the scene the player clicked or tapped the canvas; the next frame checks whether a merchant was hit (docs/adr/ADR-043 D3). Called from the page, not from the frame.</summary>
    /// <param name="x">Click x, canvas CSS pixels.</param>
    /// <param name="y">Click y, canvas CSS pixels.</param>
    public static void PointerClicked(int x, int y) => s_scene?.Pointer(x, y);

    /// <summary>Raised from the frame when the player pressed a panel key: the panel to show or hide.</summary>
    public static event Action<PanelKind>? PanelToggleRequested;

    /// <summary>Raised from the frame when the player pressed the action key next to a shop: the shop's id.</summary>
    public static event Action<int>? ShopRequested;
    /// <summary>Raised from the frame when the player pressed the action key next to the mine elevator.</summary>
    public static event Action? ElevatorRequested;

    /// <summary>Raised from the frame when the player walked out of reach of a counter: the counter's id.</summary>
    public static event Action<int>? CounterLeft;

    /// <summary>Raised from the frame when the player pressed the action key facing a furnace: the furnace's object id.</summary>
    public static event Action<int>? FurnaceRequested;

    /// <summary>Raised once from the frame, the first time the player walks (the first-run hints, docs/adr/ADR-033 D3).</summary>
    public static event Action? FirstMoved;

    /// <summary>Raised once from the frame, the first time the player presses the action key.</summary>
    public static event Action? FirstActed;

    /// <summary>Raised from the frame when the player pressed the close key (Escape).</summary>
    public static event Action? PanelCloseRequested;

    /// <summary>
    /// Loads everything the first map needs and starts the loop on the given canvas. With a network, the first map is the one
    /// the server chose (where the player was last); the farm only when the server does not answer in time.
    /// </summary>
    /// <param name="http">Client whose base address is the app root.</param>
    /// <param name="canvasId">Id of the canvas element.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>A task that completes once the loop is running.</returns>
    public static async Task StartAsync(HttpClient http, string canvasId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        TextureCache textures = new(WebRenderBackend.LoadTextureAsync);
        PlayerActions? actions = s_farm is not null && s_inventory is not null && s_market is not null ? new PlayerActions(s_farm, s_inventory, s_market, s_progress, s_chests, s_buildings, s_audio, s_combat, s_calendar) : null;
        actions?.PanelToggleRequested += kind => PanelToggleRequested?.Invoke(kind);
        actions?.ShopRequested += shopId => ShopRequested?.Invoke(shopId);
        actions?.CounterLeft += counterId => CounterLeft?.Invoke(counterId);
        actions?.ElevatorRequested += () => ElevatorRequested?.Invoke();
        actions?.FurnaceRequested += objectId => FurnaceRequested?.Invoke(objectId);
        actions?.PanelCloseRequested += () => PanelCloseRequested?.Invoke();
        List<IMapDecoratorProvider> providers = [new PropDecoratorProvider(), new NpcDecoratorProvider()];
        if (actions is not null && s_farm is not null)
        {
            providers.Add(new FarmDecoratorProvider(s_farm, actions, s_calendar, s_placeable));
        }

        if (s_coop is not null)
        {
            providers.Add(new LockedLandDecoratorProvider(s_coop));
        }

        if (s_nodes is not null)
        {
            providers.Add(new NodeDecoratorProvider(s_nodes));
        }

        PlacementActions? placement = null;
        if (s_placeable is not null && s_placement is not null && s_inventory is not null)
        {
            placement = new PlacementActions(s_placeable, s_inventory, s_placement, s_farm, s_nodes);
            providers.Add(new PlacedObjectsDecoratorProvider(s_placeable, placement, s_furnaces, s_buildings));
        }

        if (s_groundItems is not null)
        {
            providers.Add(new GroundItemsDecoratorProvider(s_groundItems));
        }

        if (s_buildings is not null)
        {
            providers.Add(new AnimalsDecoratorProvider(s_buildings, s_calendar));
        }

        LabelRegistry labelRegistry = new(WebRenderBackend.UploadLabel, capacity: 2048);
        providers.Add(new CritterDecoratorProvider(s_calendar));
        providers.Add(new WayfindingDecoratorProvider(labelRegistry, s_text));
        CombatLabels? combatLabels = s_combat is not null ? new CombatLabels(labelRegistry, s_text) : null;

        if (s_combat is not null)
        {
            providers.Add(new MonstersDecoratorProvider(s_combat, combatLabels));
        }

        if (s_network is not null)
        {
            providers.Add(new BridgeDecoratorProvider(s_network.IsUnlocked));
        }

        MapLoader loader = new(http, textures, providers, s_network is null ? null : s_network.IsUnlocked);

        Task<int[]> farmerSheets = Task.WhenAll(
            textures.LoadAsync(FarmerUrl + "body.png"),
            textures.LoadAsync(FarmerUrl + "pants.png"),
            textures.LoadAsync(FarmerUrl + "shirts.png"),
            textures.LoadAsync(FarmerUrl + "hair.png"),
            textures.LoadAsync(FarmerUrl + "hats.png"),
            textures.LoadAsync(ItemIcons.ToolsSheet),
            textures.LoadAsync(ItemIcons.WeaponsSheet),
            textures.LoadAsync(ItemIcons.ObjectsSheet));

        MapEntry? entry = await WaitForEntryAsync(cancellationToken).ConfigureAwait(false);
        MapSpec spec = entry is { } answered && GameMaps.TryGet(answered.MapId, out MapSpec? chosen)
            ? chosen
            : GameMaps.TryGet(GameMaps.DefaultId, out MapSpec? fallback) ? fallback : throw new InvalidOperationException("The default map is not in the catalogue.");
        MapStart start = entry is { } place && spec.Id == place.MapId ? MapStart.At(place) : MapStart.NearTile(spec.SpawnTileX, spec.SpawnTileY);

        MapContent content = await loader.LoadAsync(spec.Id, cancellationToken).ConfigureAwait(false);
        int[] sheets = await farmerSheets.ConfigureAwait(false);
        FarmerTextures farmer = new(sheets[0], sheets[1], sheets[2], sheets[3], sheets[4], sheets[5], sheets[6], sheets[7]);

        // Until the server's look arrives (and without a server) the default look is drawn, never one made up from an id.
        FarmerLook look = LookSprites.Resolve(LookInfo.Default);
        // Nicknames are drawn above the heads as labels uploaded once per name (never inside the frame).
        NicknameLabels? labels = s_looks is not null && s_names is not null ? new NicknameLabels(labelRegistry, s_looks, s_names) : null;
        s_scene = new MapScene(loader, content, farmer, look, start, s_network, actions, s_placeable, placement, s_looks, labels, s_nodes, s_calendar, s_audio, combatLabels);
        s_scene.WaitingChanged += mapId => OnMapWaitChanged(mapId, loader);
        loader.Progressed += mapId => OnMapProgressed(mapId, loader);
        if (s_audio is not null)
        {
            // Not awaited: the game starts at once and the sounds arrive when they are decoded (a file that fails stays silent).
            _ = GameAudio.LoadAsync();
        }

        await WebRenderBackend.StartAsync(canvasId, "Nosum.Wasm.dll", "Nosum.Wasm.Game.GameLoop.Frame").ConfigureAwait(false);
    }

    // The black screen waits on a map: the overlay shows that map's progress until the scene swaps to it.
    private static void OnMapWaitChanged(string? mapId, MapLoader loader)
    {
        s_waitingMapId = mapId;
        if (mapId is null)
        {
            s_loading?.End();
            return;
        }

        s_loading?.Begin();
        s_loading?.SetPercent(loader.PercentOf(mapId));
    }

    private static void OnMapProgressed(string mapId, MapLoader loader)
    {
        if (string.Equals(mapId, s_waitingMapId, StringComparison.Ordinal))
        {
            s_loading?.SetPercent(loader.PercentOf(mapId));
        }
    }

    // The join answer names the map to load first. Polls on the async side, before the first frame exists.
    private static async Task<MapEntry?> WaitForEntryAsync(CancellationToken cancellationToken)
    {
        if (s_network is null)
        {
            return null;
        }

        long deadline = Environment.TickCount64 + (long)s_entryTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (s_network.TryTakeEntry(out MapEntry entry))
            {
                return entry;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Called by JS once per animation frame.</summary>
    /// <param name="timeMs">requestAnimationFrame timestamp, milliseconds.</param>
    /// <param name="keys">Held directions (<see cref="InputState"/> bits) and the keys pressed since the last frame, packed by <see cref="InputPacking"/>.</param>
    /// <param name="width">Canvas width, CSS pixels.</param>
    /// <param name="height">Canvas height, CSS pixels.</param>
    /// <param name="pointerX">Mouse x over the canvas, CSS pixels (drag-to-sow).</param>
    /// <param name="pointerY">Mouse y over the canvas, CSS pixels (drag-to-sow).</param>
    /// <param name="pointerHeld">1 while the mouse button is down over the canvas, otherwise 0.</param>
    [JSExport]
    internal static void Frame(double timeMs, int keys, int width, int height, int pointerX, int pointerY, int pointerHeld)
    {
        if (s_scene is null || width <= 0 || height <= 0)
        {
            return;
        }

        double seconds = s_lastTime < 0 ? 0 : (timeMs - s_lastTime) / 1000.0;
        s_lastTime = timeMs;
        InputPacking.Unpack(keys, out InputState held, out InputEdges edges);
        if (s_connectionBlocked)
        {
            held = InputState.None;
            edges = InputEdges.None;
        }
        else if (s_overlayOpen)
        {
            held = InputState.None;
            edges &= InputEdges.Close;
        }
        else if (s_modalOpen)
        {
            held = InputState.None;
            edges &= PanelKeysMask;
        }

        if (!s_moved && held != InputState.None)
        {
            s_moved = true;
            FirstMoved?.Invoke();
        }

        if (!s_acted && (edges & InputEdges.Action) != 0)
        {
            s_acted = true;
            FirstActed?.Invoke();
        }

        // A drag works the farm only while the world takes input: no overlay, dialog or lost connection in front of it.
        s_scene.SetPointer(pointerX, pointerY, pointerHeld != 0 && !s_connectionBlocked && !s_overlayOpen && !s_modalOpen);
        s_scene.Frame(seconds, held, edges, width, height, s_backend);
    }
}
