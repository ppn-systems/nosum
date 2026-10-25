// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;

namespace Nosum.Wasm.Game;

/// <summary>
/// Loads maps by id and keeps them: the map file, its tilesheets (through the shared <see cref="TextureCache"/>) and
/// whatever the <see cref="IMapDecoratorProvider"/>s add. Loading runs on the async side; the frame only asks
/// <see cref="TryGet"/>, which never waits. A map is loaded once and kept for the session (the catalogue is small).
/// </summary>
internal sealed class MapLoader
{
    private const string MapsUrl = "assets/Maps/";
    private static readonly TimeSpan s_retryAfter = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly TextureCache _textures;
    private readonly IReadOnlyList<IMapDecoratorProvider> _providers;
    private readonly Dictionary<string, MapContent> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _loading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _retryAt = new(StringComparer.Ordinal);

    /// <summary>Initializes the loader.</summary>
    /// <param name="http">Client whose base address is the app root.</param>
    /// <param name="textures">Shared texture loading.</param>
    /// <param name="providers">Adds map-specific drawing to every loaded map.</param>
    public MapLoader(HttpClient http, TextureCache textures, IReadOnlyList<IMapDecoratorProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(providers);

        _http = http;
        _textures = textures;
        _providers = providers;
    }

    /// <summary>Returns a loaded map without waiting.</summary>
    /// <param name="mapId">Map id from the catalogue.</param>
    /// <param name="content">The map when it is loaded.</param>
    /// <returns><see langword="true"/> when the map is ready to show.</returns>
    public bool TryGet(string mapId, [NotNullWhen(true)] out MapContent? content) => _loaded.TryGetValue(mapId, out content);

    /// <summary>Starts loading a map in the background if it is neither loaded nor loading; a failed load is retried after a pause.</summary>
    /// <param name="mapId">Map id; ids the catalogue does not know are ignored.</param>
    public void Prefetch(string mapId)
    {
        if (_loaded.ContainsKey(mapId) || _loading.ContainsKey(mapId) || !GameMaps.TryGet(mapId, out MapSpec? spec))
        {
            return;
        }

        if (_retryAt.TryGetValue(mapId, out long when) && Environment.TickCount64 < when)
        {
            return;
        }

        _loading[mapId] = this.LoadIntoCacheAsync(spec);
    }

    /// <summary>Loads a map and waits for it; used once at start-up, before the first frame.</summary>
    /// <param name="mapId">Map id from the catalogue.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>The loaded map.</returns>
    /// <exception cref="ArgumentException">The catalogue has no such map.</exception>
    public async Task<MapContent> LoadAsync(string mapId, CancellationToken cancellationToken)
    {
        if (!GameMaps.TryGet(mapId, out MapSpec? spec))
        {
            throw new ArgumentException("Unknown map '" + mapId + "'.", nameof(mapId));
        }

        if (_loaded.TryGetValue(mapId, out MapContent? ready))
        {
            return ready;
        }

        MapContent content = await this.ReadAsync(spec, cancellationToken).ConfigureAwait(false);
        _loaded[mapId] = content;
        return content;
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed background load is reported and retried; it must not crash the frame loop.")]
    private async Task LoadIntoCacheAsync(MapSpec spec)
    {
        try
        {
            _loaded[spec.Id] = await this.ReadAsync(spec, CancellationToken.None).ConfigureAwait(false);
            _ = _retryAt.Remove(spec.Id);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Could not load map '{spec.Id}': {ex.Message}").ConfigureAwait(false);
            _retryAt[spec.Id] = Environment.TickCount64 + (long)s_retryAfter.TotalMilliseconds;
        }
        finally
        {
            _ = _loading.Remove(spec.Id);
        }
    }

    private async Task<MapContent> ReadAsync(MapSpec spec, CancellationToken cancellationToken)
    {
        string xml = await _http.GetStringAsync(new Uri(MapsUrl + spec.TmxFile, UriKind.Relative), cancellationToken).ConfigureAwait(false);
        TileMap map = TmxReader.Parse(xml);

        // A sheet is named relative to the folder of the map file (the mine's floor plans live in Mines/ and name "mine").
        int slash = spec.TmxFile.LastIndexOf('/');
        string folder = slash < 0 ? string.Empty : spec.TmxFile[..(slash + 1)];
        Task<int>[] sheets = new Task<int>[map.TileSets.Count];
        for (int i = 0; i < sheets.Length; i++)
        {
            string source = map.TileSets[i].ImageSource.Replace('\\', '/');
            sheets[i] = _textures.LoadAsync(MapsUrl + folder + (source.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? source : source + ".png"));
        }

        List<IMapDecorator> decorators = [];
        foreach (IMapDecoratorProvider provider in _providers)
        {
            if (await provider.CreateAsync(spec, map, _textures, cancellationToken).ConfigureAwait(false) is { } decorator)
            {
                decorators.Add(decorator);
            }
        }

        int[] tileSetTextures = await Task.WhenAll(sheets).ConfigureAwait(false);
        return new MapContent(spec, map, tileSetTextures, Nosum.Rendering.TileLayerPlan.For(map), spec.BuildWalkGrid(map), spec.ResolveWarps(map), [.. decorators]);
    }
}
