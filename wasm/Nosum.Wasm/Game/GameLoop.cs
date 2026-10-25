// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Player;
using Nosum.Rendering.Web;

namespace Nosum.Wasm.Game;

/// <summary>
/// Loads the map the server put the player on, then runs the frame loop: JS calls <see cref="Frame"/> once per
/// animation frame and the scene submits one draw list back (docs/adr/ADR-002). Later maps are loaded by the
/// scene's <see cref="MapLoader"/> in the background when the server changes the player's map.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class GameLoop
{
    private const string FarmerUrl = "assets/Characters/Farmer/";

    private static readonly TimeSpan s_entryTimeout = TimeSpan.FromSeconds(10);

    private static readonly WebRenderBackend s_backend = new();
    private static MapScene? s_scene;
    private static PlayerChannel? s_network;
    private static FarmChannel? s_farm;
    private static InventoryChannel? s_inventory;
    private static double s_lastTime = -1;

    /// <summary>Connects the game to the server; call before <see cref="StartAsync"/>. Without it the farm is played offline.</summary>
    /// <param name="channel">The player channel.</param>
    public static void UseNetwork(PlayerChannel channel) => s_network = channel;

    /// <summary>Connects farming to the game: the farm mirror and the held item. Call before <see cref="StartAsync"/>.</summary>
    /// <param name="farm">The farm mirror and action sender.</param>
    /// <param name="inventory">The inventory mirror.</param>
    public static void UseFarming(FarmChannel farm, InventoryChannel inventory)
    {
        s_farm = farm;
        s_inventory = inventory;
    }

    /// <summary>Raised from the frame when the player pressed the inventory key.</summary>
    public static event Action? InventoryToggleRequested;

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
        PlayerActions? actions = s_farm is not null && s_inventory is not null ? new PlayerActions(s_farm, s_inventory) : null;
        actions?.InventoryToggleRequested += () => InventoryToggleRequested?.Invoke();
        List<IMapDecoratorProvider> providers = [new PropDecoratorProvider()];
        if (actions is not null && s_farm is not null)
        {
            providers.Add(new FarmDecoratorProvider(s_farm, actions));
        }

        MapLoader loader = new(http, textures, providers);

        Task<int[]> farmerSheets = Task.WhenAll(
            textures.LoadAsync(FarmerUrl + "farmer_base.png"),
            textures.LoadAsync(FarmerUrl + "pants.png"),
            textures.LoadAsync(FarmerUrl + "shirts.png"),
            textures.LoadAsync(FarmerUrl + "hairstyles.png"));

        MapEntry? entry = await WaitForEntryAsync(cancellationToken).ConfigureAwait(false);
        MapSpec spec = entry is { } answered && GameMaps.TryGet(answered.MapId, out MapSpec? chosen)
            ? chosen
            : GameMaps.TryGet(GameMaps.DefaultId, out MapSpec? fallback) ? fallback : throw new InvalidOperationException("The default map is not in the catalogue.");
        MapStart start = entry is { } place && spec.Id == place.MapId ? MapStart.At(place) : MapStart.NearTile(spec.SpawnTileX, spec.SpawnTileY);

        MapContent content = await loader.LoadAsync(spec.Id, cancellationToken).ConfigureAwait(false);
        int[] sheets = await farmerSheets.ConfigureAwait(false);
        FarmerTextures farmer = new(sheets[0], sheets[1], sheets[2], sheets[3]);

        FarmerLook look = new(Hairstyle: 1, HairTint: unchecked((int)0x6B4226FF), Shirt: 3, Pants: 0, PantsTint: unchecked((int)0x3E5F8AFF));
        s_scene = new MapScene(loader, content, farmer, look, start, s_network, actions);

        await WebRenderBackend.StartAsync(canvasId, "Nosum.Wasm.dll", "Nosum.Wasm.Game.GameLoop.Frame").ConfigureAwait(false);
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
            if (s_network.TryPeekEntry(out MapEntry entry))
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
    [JSExport]
    internal static void Frame(double timeMs, int keys, int width, int height)
    {
        if (s_scene is null || width <= 0 || height <= 0)
        {
            return;
        }

        double seconds = s_lastTime < 0 ? 0 : (timeMs - s_lastTime) / 1000.0;
        s_lastTime = timeMs;
        InputPacking.Unpack(keys, out InputState held, out InputEdges edges);
        s_scene.Frame(seconds, held, edges, width, height, s_backend);
    }
}
