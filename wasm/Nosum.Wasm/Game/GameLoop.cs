// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Farm;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Player;
using Nosum.Rendering;
using Nosum.Rendering.Web;

namespace Nosum.Wasm.Game;

/// <summary>
/// Loads the farm map and sheets, then runs the frame loop: JS calls <see cref="Frame"/> once per
/// animation frame and the scene submits one draw list back (docs/adr/ADR-002).
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class GameLoop
{
    private const string MapsUrl = "assets/Maps/";
    private const string FarmerUrl = "assets/Characters/Farmer/";
    private const string BuildingsUrl = "assets/Buildings/";
    private const string TileSheetsUrl = "assets/TileSheets/";
    private const string TerrainUrl = "assets/TerrainFeatures/";

    private static readonly WebRenderBackend s_backend = new();
    private static FarmScene? s_scene;
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
    /// Loads everything and starts the loop on the given canvas.
    /// </summary>
    /// <param name="http">Client whose base address is the app root.</param>
    /// <param name="canvasId">Id of the canvas element.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>A task that completes once the loop is running.</returns>
    public static async Task StartAsync(HttpClient http, string canvasId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        TileMap map = TmxReader.Parse(await http.GetStringAsync(new Uri(MapsUrl + "Farm.tmx", UriKind.Relative), cancellationToken).ConfigureAwait(false));

        int[] tileSetTextures = new int[map.TileSets.Count];
        Task[] loads = new Task[map.TileSets.Count + 10];
        for (int i = 0; i < map.TileSets.Count; i++)
        {
            string source = map.TileSets[i].ImageSource;
            tileSetTextures[i] = i;
            loads[i] = WebRenderBackend.LoadTextureAsync(i, MapsUrl + (source.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? source : source + ".png"));
        }

        int next = map.TileSets.Count;
        FarmerTextures farmer = new(next, next + 1, next + 2, next + 3);
        loads[next] = WebRenderBackend.LoadTextureAsync(farmer.Body, FarmerUrl + "farmer_base.png");
        loads[next + 1] = WebRenderBackend.LoadTextureAsync(farmer.Pants, FarmerUrl + "pants.png");
        loads[next + 2] = WebRenderBackend.LoadTextureAsync(farmer.Shirts, FarmerUrl + "shirts.png");
        loads[next + 3] = WebRenderBackend.LoadTextureAsync(farmer.Hair, FarmerUrl + "hairstyles.png");
        int houses = next + 4;
        loads[houses] = WebRenderBackend.LoadTextureAsync(houses, BuildingsUrl + "houses.png");
        loads[houses + 1] = WebRenderBackend.LoadTextureAsync(houses + 1, BuildingsUrl + "Greenhouse.png");
        loads[houses + 2] = WebRenderBackend.LoadTextureAsync(houses + 2, BuildingsUrl + "Shipping Bin.png");
        int crops = houses + 3;
        loads[crops] = WebRenderBackend.LoadTextureAsync(crops, TileSheetsUrl + "crops.png");
        loads[crops + 1] = WebRenderBackend.LoadTextureAsync(crops + 1, TerrainUrl + "hoeDirt.png");
        loads[crops + 2] = WebRenderBackend.LoadTextureAsync(crops + 2, TargetCursor.DataUrl);
        await Task.WhenAll(loads).ConfigureAwait(false);

        FarmerLook look = new(Hairstyle: 1, HairTint: unchecked((int)0x6B4226FF), Shirt: 3, Pants: 0, PantsTint: unchecked((int)0x3E5F8AFF));
        PropLayer props = new(
            FarmProps.Placements,
            sheet => sheet switch
            {
                FarmProps.HousesSheet => houses,
                FarmProps.GreenhouseSheet => houses + 1,
                FarmProps.ShippingBinSheet => houses + 2,
                _ => -1,
            },
            map.TileWidth);
        FarmLayer? farmLayer = s_farm is null ? null : new FarmLayer(s_farm, crops + 1, crops, crops + 2, map.TileWidth);
        s_scene = new FarmScene(map, tileSetTextures, farmer, look, props, FarmProps.FarmhouseDoorFront.X, FarmProps.FarmhouseDoorFront.Y, s_network, farmLayer, s_farm, s_inventory);
        s_scene.InventoryToggleRequested += () => InventoryToggleRequested?.Invoke();

        await WebRenderBackend.StartAsync(canvasId, "Nosum.Wasm.dll", "Nosum.Wasm.Game.GameLoop.Frame").ConfigureAwait(false);
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
