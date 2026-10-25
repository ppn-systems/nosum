// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Nosum.Core;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// One map of the world as data: its stable id, its Tiled file, where new arrivals stand and what
/// the file does not say (buildings, hand-made exits). Tilesets come from the file itself. The server
/// and the client both build the walk grid and the exits from this, so they agree on every collision
/// and every door.
/// </summary>
public sealed class MapSpec
{
    /// <summary>Initializes a map spec.</summary>
    /// <param name="id">Stable lower-case key, for example <c>farm</c>; the only map name that crosses the wire.</param>
    /// <param name="tmxFile">File name of the <c>.tmx</c> inside the maps folder (a name, never a path).</param>
    /// <param name="spawnTileX">Preferred tile x for a player with no saved place.</param>
    /// <param name="spawnTileY">Preferred tile y for a player with no saved place.</param>
    /// <param name="props">Buildings and props; their footprints block walking.</param>
    /// <param name="openTiles">Tiles to make walkable again after the footprints block them (doorsteps in a building's footprint).</param>
    /// <param name="extraWarps">Exits the file does not list.</param>
    public MapSpec(
        string id,
        string tmxFile,
        int spawnTileX,
        int spawnTileY,
        IReadOnlyList<PropPlacement>? props = null,
        IReadOnlyList<(int X, int Y)>? openTiles = null,
        IReadOnlyList<MapWarp>? extraWarps = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(tmxFile);
        if (tmxFile.AsSpan().IndexOfAny('/', '\\') >= 0)
        {
            throw new ArgumentException("A map file is a name inside the maps folder, not a path.", nameof(tmxFile));
        }

        this.Id = id;
        this.TmxFile = tmxFile;
        this.SpawnTileX = spawnTileX;
        this.SpawnTileY = spawnTileY;
        this.Props = props ?? [];
        this.OpenTiles = openTiles ?? [];
        this.ExtraWarps = extraWarps ?? [];
    }

    /// <summary>Gets the stable map key.</summary>
    public string Id { get; }

    /// <summary>Gets the <c>.tmx</c> file name.</summary>
    public string TmxFile { get; }

    /// <summary>Gets the preferred spawn tile x.</summary>
    public int SpawnTileX { get; }

    /// <summary>Gets the preferred spawn tile y.</summary>
    public int SpawnTileY { get; }

    /// <summary>Gets the buildings and props standing on the map.</summary>
    public IReadOnlyList<PropPlacement> Props { get; }

    /// <summary>Gets the tiles opened inside prop footprints.</summary>
    public IReadOnlyList<(int X, int Y)> OpenTiles { get; }

    /// <summary>Gets the exits the file does not list.</summary>
    public IReadOnlyList<MapWarp> ExtraWarps { get; }

    /// <summary>Builds the walk grid: the map's own rules, the prop footprints, then the opened tiles.</summary>
    /// <param name="map">The parsed map.</param>
    /// <returns>The grid; client and server must both use this.</returns>
    public WalkGrid BuildWalkGrid(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        WalkGrid grid = WalkGrid.FromMap(map);
        grid.BlockFootprints(this.Props);
        foreach ((int x, int y) in this.OpenTiles)
        {
            grid.Open(x, y);
        }

        return grid;
    }

    /// <summary>
    /// Returns the map's exits: the file's <c>Warp</c> property plus <see cref="ExtraWarps"/>, with targets resolved to map ids.
    /// An exit to a map the world does not host is dropped, so its edge is a plain wall.
    /// </summary>
    /// <param name="map">The parsed map.</param>
    /// <returns>The exits.</returns>
    /// <exception cref="FormatException">The map's <c>Warp</c> property is malformed.</exception>
    public List<MapWarp> ResolveWarps(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        List<MapWarp> resolved = [];
        map.Properties.TryGetValue(WarpParser.PropertyName, out string? property);
        foreach (MapWarp warp in WarpParser.Parse(property).Concat(this.ExtraWarps))
        {
            if (GameMaps.Resolve(warp.TargetMap) is { } target)
            {
                resolved.Add(warp with { TargetMap = target.Id });
            }
        }

        return resolved;
    }
}
