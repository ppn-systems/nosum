// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nosum.Wasm.Game;

/// <summary>
/// Gives every image file one texture id and loads it once, however many maps and sprites ask for it
/// (the outdoor tilesheet is used by every outdoor map). Loading is asynchronous and never happens inside the frame loop.
/// </summary>
internal sealed class TextureCache
{
    private readonly Func<int, string, Task> _load;
    private readonly Dictionary<string, (int Id, Task Loaded)> _byUrl = new(StringComparer.Ordinal);
    private int _nextId;

    /// <summary>Initializes the cache.</summary>
    /// <param name="load">Downloads an image and registers it under a texture id (the render backend).</param>
    public TextureCache(Func<int, string, Task> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <summary>Gets the number of distinct images requested so far.</summary>
    public int Count => _byUrl.Count;

    /// <summary>Reserves a texture id that no image uses: a render target the backend creates on its first draw (docs/adr/ADR-086).</summary>
    /// <returns>The id.</returns>
    public int ReserveId() => _nextId++;

    /// <summary>Loads an image, or joins the load already running for it.</summary>
    /// <param name="url">Image URL; the same URL always gets the same id.</param>
    /// <returns>A task with the texture id, ready to draw once the task has completed.</returns>
    public async Task<int> LoadAsync(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        if (!_byUrl.TryGetValue(url, out (int Id, Task Loaded) entry))
        {
            int id = _nextId++;
            entry = (id, _load(id, url));
            _byUrl[url] = entry;
        }

        try
        {
            await entry.Loaded.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Forget the failed load so a later request tries again. The id stays unused.
            _ = _byUrl.Remove(url);
            throw;
        }

        return entry.Id;
    }
}
