// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Nosum.Core;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

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
    /// <param name="placeable">Whether players may place objects (furniture, chests, fences, ...) on its free ground: the farmhouse, the farm and co-op land (docs/adr/ADR-024 D3).</param>
    /// <param name="shops">Where shops are served on this map (docs/adr/ADR-013 amendments).</param>
    /// <param name="isCoop">Whether each co-op has its own copy of the map, owned by the co-op and open to its members only (docs/adr/ADR-020).</param>
    /// <param name="mineFloor">The floor number (1 and up) when the map is a floor of the mine: a per-player instance whose nodes are built in memory each game day (docs/adr/ADR-026 D3); zero for any other map.</param>
    /// <param name="elevator">Where the mine elevator is served on this map, or <see langword="null"/> when there is none.</param>
    /// <param name="interiorOf">The kind of farm building this map is the inside of; there is one instance per building, keyed by the building's id (docs/adr/ADR-028 D2).</param>
    /// <param name="indoors">Whether the map is a room or a cave: no weather falls on it, rain does not water its soil and it is lit (docs/adr/ADR-027).</param>
    /// <param name="dropFileWarps">Source tiles of exits in the file's own <c>Warp</c> property that this map does not use (the file's exit to a place the world hosts differently, docs/adr/ADR-037 D1).</param>
    /// <param name="gates">Tiles that stay blocked until a community unlock is set (docs/adr/ADR-037 D4).</param>
    /// <param name="nodeSites">Resource node sites the file does not mark, one by one.</param>
    /// <param name="scatter">Resource node sites the file does not mark, spread over rectangles (<see cref="NodeScatter"/>).</param>
    /// <param name="forageAreas">Rectangles whose walkable tiles are forage spots (<see cref="ForageArea"/>).</param>
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
        ElevatorPoint? elevator = null,
        BuildingKind interiorOf = BuildingKind.None,
        bool indoors = false,
        IReadOnlyList<(int X, int Y)>? dropFileWarps = null,
        IReadOnlyList<MapGate>? gates = null,
        IReadOnlyList<NodeSite>? nodeSites = null,
        IReadOnlyList<NodeScatter>? scatter = null,
        IReadOnlyList<ForageArea>? forageAreas = null)
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
        this.InteriorOf = interiorOf;
        this.Indoors = indoors;
        this.DropFileWarps = dropFileWarps ?? [];
        this.Gates = gates ?? [];
        this.ExtraNodeSites = nodeSites ?? [];
        this.Scatter = scatter ?? [];
        this.ForageAreas = forageAreas ?? [];
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
    /// <summary>Gets the kind of building this map is the inside of, or <see cref="BuildingKind.None"/> for every other map.</summary>
    public BuildingKind InteriorOf { get; }

    /// <summary>Gets whether this map is the inside of a farm building: its instance owner is the building's id, not a player (docs/adr/ADR-028 D2).</summary>
    public bool IsInterior => this.InteriorOf != BuildingKind.None;

    /// <summary>Gets whether the map is indoors: no rain or snow, rain does not water soil there, and daylight does not tint it (docs/adr/ADR-027).</summary>
    public bool Indoors { get; }

    /// <summary>Gets the source tiles of the file's exits that this map drops.</summary>
    public IReadOnlyList<(int X, int Y)> DropFileWarps { get; }

    /// <summary>Gets the tiles that are blocked until an unlock is set.</summary>
    public IReadOnlyList<MapGate> Gates { get; }

    /// <summary>Gets the resource node sites added to the file's own.</summary>
    public IReadOnlyList<NodeSite> ExtraNodeSites { get; }

    /// <summary>Gets the node scatters that add sites to the file's own.</summary>
    public IReadOnlyList<NodeScatter> Scatter { get; }

    /// <summary>Gets the rectangles that add forage spots to the file's own.</summary>
    public IReadOnlyList<ForageArea> ForageAreas { get; }

    /// <summary>Gets whether players may place objects on this map: the farmhouse, the farm and co-op land.</summary>
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

    /// <summary>Builds the walk grid: the map's own rules, the prop footprints, then the opened tiles; gates are attached last.</summary>
    /// <param name="map">The parsed map.</param>
    /// <param name="unlocked">Whether a community unlock key is set, asked every time a gated tile is tested; <see langword="null"/> keeps every gate shut.</param>
    /// <returns>The grid; client and server must both use this.</returns>
    public WalkGrid BuildWalkGrid(TileMap map, Func<string, bool>? unlocked = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        WalkGrid grid = WalkGrid.FromMap(map);
        grid.BlockFootprints(this.Props);
        foreach ((int x, int y) in this.OpenTiles)
        {
            grid.Open(x, y);
        }

        foreach (MapGate gate in this.Gates)
        {
            grid.AddGate(new WalkGate(gate.Key, gate.Tiles, grid.Width, unlocked));
        }

        return grid;
    }

    /// <summary>Returns the resource node sites of the map: the <c>Paths</c> layer's, the single sites and the scatters.</summary>
    /// <param name="map">The parsed map.</param>
    /// <param name="grid">The map's walk grid (scatters keep to open ground).</param>
    /// <returns>The sites.</returns>
    public List<NodeSite> ResolveNodeSites(TileMap map, WalkGrid grid)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(grid);

        List<NodeSite> sites = NodeSites.Read(map);
        sites.AddRange(this.ExtraNodeSites);
        ulong seed = NodeRandom.HashOf(this.Id);
        for (int i = 0; i < this.Scatter.Count; i++)
        {
            sites.AddRange(this.Scatter[i].Sites(grid, NodeRandom.Mix(seed, i, 7)));
        }

        return sites;
    }

    /// <summary>Returns the forage spots of the map: the glyphs of the <c>Paths</c> layer (pool <see cref="ForagePools.Woodland"/>) and the walkable tiles of the forage areas.</summary>
    /// <param name="map">The parsed map.</param>
    /// <param name="grid">The map's walk grid.</param>
    /// <returns>The spots, <see cref="NodeSite.Variant"/> holding the pool.</returns>
    public List<NodeSite> ResolveForageSites(TileMap map, WalkGrid grid)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(grid);

        List<NodeSite> sites = NodeSites.ReadForage(map);
        foreach (ForageArea area in this.ForageAreas)
        {
            for (int y = area.Y0; y <= area.Y1; y++)
            {
                for (int x = area.X0; x <= area.X1; x++)
                {
                    if (!grid.IsBlocked(x, y))
                    {
                        sites.Add(new NodeSite(x, y, NodeKind.Forage, area.Pool));
                    }
                }
            }
        }

        return sites;
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
        foreach (MapWarp warp in WarpParser.Parse(property).Where(w => !this.DropFileWarps.Contains((w.FromX, w.FromY))).Concat(this.ExtraWarps))
        {
            if (GameMaps.Resolve(warp.TargetMap) is { } target)
            {
                resolved.Add(warp with { TargetMap = target.Id });
            }
        }

        return resolved;
    }
}
