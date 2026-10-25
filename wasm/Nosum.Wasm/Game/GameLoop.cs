// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
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

    private static readonly WebRenderBackend s_backend = new();
    private static FarmScene? s_scene;
    private static double s_lastTime = -1;

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
        Task[] loads = new Task[map.TileSets.Count + 4];
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
        await Task.WhenAll(loads).ConfigureAwait(false);

        FarmerLook look = new(Hairstyle: 1, HairTint: unchecked((int)0x6B4226FF), Shirt: 3, Pants: 0, PantsTint: unchecked((int)0x3E5F8AFF));
        s_scene = new FarmScene(map, tileSetTextures, farmer, look, startTileX: 64, startTileY: 17);

        await WebRenderBackend.StartAsync(canvasId, "Nosum.Wasm.dll", "Nosum.Wasm.Game.GameLoop.Frame").ConfigureAwait(false);
    }

    /// <summary>Called by JS once per animation frame.</summary>
    /// <param name="timeMs">requestAnimationFrame timestamp, milliseconds.</param>
    /// <param name="keys">Held directions as <see cref="InputState"/> bits.</param>
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
        s_scene.Frame(seconds, (InputState)keys, width, height, s_backend);
    }
}
