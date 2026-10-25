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
    /// <param name="tmxFile">File name of the <c>.tmx</c> inside the maps folder, or inside one sub-folder of it such as <c>Mines/1.tmx</c> (a fixed name from this catalogue, never client input).</param>
    /// <param name="spawnTileX">Preferred tile x for a player with no saved place.</param>
    /// <param name="spawnTileY">Preferred tile y for a player with no saved place.</param>
    /// <param name="props">Buildings and props; their footprints block walking.</param>
    /// <param name="openTiles">Tiles to make walkable again after the footprints block them (doorsteps in a building's footprint).</param>
    /// <param name="extraWarps">Exits the file does not list.</param>
    /// <param name="farmable">Whether diggable ground on this map can be tilled and planted.</param>
    /// <param name="isPrivate">Whether every player has their own copy of the map and nobody else enters it (docs/adr/ADR-015).</param>
    /// <param name="placeable">Whether players may place furniture on its free floor.</param>
    /// <param name="shops">Where shops are served on this map (docs/adr/ADR-013 amendments).</param>
    /// <param name="isCoop">Whether each co-op has its own copy of the map, owned by the co-op and open to its members only (docs/adr/ADR-020).</param>
    /// <param name="mineFloor">The floor number (1 and up) when the map is a floor of the mine: a per-player instance whose nodes are built in memory each game day (docs/adr/ADR-026 D3); zero for any other map.</param>
    /// <param name="elevator">Where the mine elevator is served on this map, or <see langword="null"/> when there is none.</param>
    public MapSpec(
        string id,
        string tmxFile,
        int spawnTileX,
        int spawnTileY,
        IReadOnlyList<PropPlacement>? props = null,
        IReadOnlyList<(int X, int Y)>? openTiles = null,
        IReadOnlyList<MapWarp>? extraWarps = null,
        bool farmable = false,
        bool isPrivate = false,
        bool placeable = false,
        IReadOnlyList<ShopPoint>? shops = null,
        bool isCoop = false,
        int mineFloor = 0,
        ElevatorPoint? elevator = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(tmxFile);
        ArgumentOutOfRangeException.ThrowIfNegative(mineFloor);
        if (!IsPlainFileName(tmxFile))
        {
            throw new ArgumentException("A map file is a name inside the maps folder (or one sub-folder of it), not a path.", nameof(tmxFile));
        }

        this.Id = id;
        this.TmxFile = tmxFile;
        this.SpawnTileX = spawnTileX;
        this.SpawnTileY = spawnTileY;
        this.Props = props ?? [];
        this.OpenTiles = openTiles ?? [];
        this.ExtraWarps = extraWarps ?? [];
        this.Farmable = farmable;
        this.IsPrivate = isPrivate;
        this.Placeable = placeable;
        this.Shops = shops ?? [];
        this.IsCoop = isCoop;
        this.MineFloor = mineFloor;
        this.Elevator = elevator;
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

    /// <summary>Gets whether diggable ground on this map can be farmed (tilled, planted, harvested).</summary>
    public bool Farmable { get; }

    /// <summary>Gets whether every player has their own instance of this map (farm, farmhouse); see <see cref="MapInstance"/>.</summary>
    public bool IsPrivate { get; }

    /// <summary>Gets whether every co-op has its own instance of this map, keyed by the co-op id (docs/adr/ADR-020).</summary>
    public bool IsCoop { get; }

    /// <summary>Gets the mine floor number (1 and up), or zero when the map is not a floor of the mine (docs/adr/ADR-026 D3).</summary>
    public int MineFloor { get; }

    /// <summary>Gets whether the map is a floor of the mine.</summary>
    public bool IsMineFloor => this.MineFloor > 0;

    /// <summary>Gets where the mine elevator is served, or <see langword="null"/> when this map has none.</summary>
    public ElevatorPoint? Elevator { get; }

    /// <summary>Gets whether players may place furniture on this map.</summary>
    public bool Placeable { get; }

    /// <summary>Gets the shop points of this map; empty when nothing is sold here.</summary>
    public IReadOnlyList<ShopPoint> Shops { get; }

    // A bare file name, or "Folder/name": no rooted path, no "..", no backslash, at most one separator.
    private static bool IsPlainFileName(string file)
    {
        if (file.Contains('\\', StringComparison.Ordinal) || file.Contains("..", StringComparison.Ordinal) || file.StartsWith('/'))
        {
            return false;
        }

        int first = file.IndexOf('/', StringComparison.Ordinal);
        return first < 0 ? file.Length > 0 : first > 0 && first == file.LastIndexOf('/') && first < file.Length - 1;
    }

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
