// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Shops;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// The catalogue of maps the world hosts, shared by the server (which loads them and decides every
/// map change) and the client (which draws them). A client names a map only by <see cref="MapSpec.Id"/>
/// and the server accepts only ids found here, so an id is never turned into a path.
/// Farm and farmhouse are private: each player has their own instance (<see cref="MapInstance"/>, docs/adr/ADR-015); the other maps are shared.
/// Adding a map: add a <see cref="MapSpec"/> below, link its <c>.tmx</c> and tilesheets in
/// <c>Nosum.Wasm.csproj</c> and <c>Nosum.Host.csproj</c> (a test checks it).
/// </summary>
public static class GameMaps
{
    /// <summary>Id of the map a new player starts on.</summary>
    public const string DefaultId = "farm";

    /// <summary>Id of the farm.</summary>
    public const string Farm = "farm";

    /// <summary>Id of the farmhouse interior.</summary>
    public const string FarmHouse = "farmhouse";

    /// <summary>Id of the bus stop east of the farm.</summary>
    public const string BusStop = "busstop";

    /// <summary>Id of the forest south of the farm.</summary>
    public const string Forest = "forest";

    // The door is the tile in the middle of the farmhouse's bottom row, inside its footprint; it is opened
    // so the farmer can walk into it. FarmProps.FarmhouseDoorFront is the tile in front of it.
    private static readonly (int X, int Y) s_farmhouseDoor = (FarmProps.FarmhouseDoorFront.X, FarmProps.FarmhouseDoorFront.Y - 1);

    /// <summary>Id of the town east of the bus stop.</summary>
    public const string Town = "town";

    /// <summary>Id of the backwoods north of the farm.</summary>
    public const string Backwoods = "backwoods";

    // The doormat in front of Pierre's store in Town.tmx (Stardew's general store; the placeholder art, docs/adr/ADR-007). Standing near it opens the shop.
    private static readonly (int X, int Y) s_townShopTile = (43, 57);

    private static readonly MapSpec[] s_all =
    [
        new(Farm, "Farm.tmx", FarmProps.FarmhouseDoorFront.X, FarmProps.FarmhouseDoorFront.Y, FarmProps.Placements, [s_farmhouseDoor], [new MapWarp(s_farmhouseDoor.X, s_farmhouseDoor.Y, FarmHouse, 3, 11)], farmable: true, isPrivate: true),
        new(FarmHouse, "FarmHouse.tmx", 3, 11, isPrivate: true, placeable: true),
        new(BusStop, "BusStop.tmx", 11, 23),
        new(Forest, "Forest.tmx", 68, 0),
        new(Town, "Town.tmx", 4, 54, shops: [new ShopPoint(ShopCatalog.GeneralStore, s_townShopTile.X, s_townShopTile.Y)]),
        new(Backwoods, "Backwoods.tmx", 14, 38),
    ];

    /// <summary>Gets every hosted map.</summary>
    public static IReadOnlyList<MapSpec> All => s_all;

    /// <summary>Looks up a map by its exact id.</summary>
    /// <param name="id">Map id as sent by a client; not trusted.</param>
    /// <param name="map">The map when found.</param>
    /// <returns><see langword="true"/> when the world hosts that map.</returns>
    public static bool TryGet(string? id, [NotNullWhen(true)] out MapSpec? map)
    {
        if (id is not null)
        {
            foreach (MapSpec spec in s_all)
            {
                if (string.Equals(spec.Id, id, StringComparison.Ordinal))
                {
                    map = spec;
                    return true;
                }
            }
        }

        map = null;
        return false;
    }

    /// <summary>Finds a map by name ignoring case, for the map names in Tiled <c>Warp</c> properties (<c>BusStop</c> is <c>busstop</c>).</summary>
    /// <param name="name">Name as written in a map file.</param>
    /// <returns>The map, or <see langword="null"/> when the world does not host it.</returns>
    public static MapSpec? Resolve(string name)
    {
        foreach (MapSpec spec in s_all)
        {
            if (string.Equals(spec.Id, name, StringComparison.OrdinalIgnoreCase))
            {
                return spec;
            }
        }

        return null;
    }
}
