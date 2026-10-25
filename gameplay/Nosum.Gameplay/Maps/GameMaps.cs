// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Shops;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// The catalogue of maps the world hosts, shared by the server (which loads them and decides every
/// map change) and the client (which draws them). A client names a map only by <see cref="MapSpec.Id"/>
/// and the server accepts only ids found here, so an id is never turned into a path.
/// Farm and farmhouse are private: each player has their own instance (<see cref="MapInstance"/>, docs/adr/ADR-015); co-op land has one per co-op (docs/adr/ADR-020); the other maps are shared.
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

    /// <summary>Id of the mine's entrance cave, a shared map north of Town with the shaft to the first floor and the elevator (docs/adr/ADR-026 D3).</summary>
    public const string Mine = "mine";

    /// <summary>Id of the co-op land: each co-op's own shared farm, entered from Town (docs/adr/ADR-020).</summary>
    public const string CoopFarm = "coopfarm";

    // Shop counters in Town.tmx: the walkable tile just south of a building's door (the doormat), where the farmer stands and presses the action key.
    // The art is Stardew's placeholder (docs/adr/ADR-007); each building is the one whose owner fits the trade:
    //  - seeds: Pierre's store, door tiles (43,56) and (44,56), doormat (43,57);
    //  - clothing: Emily and Haley's house, door tile (20,88), doormat (20,89) (Emily sews, Haley follows fashion);
    //  - furniture: JojaMart, door tiles (95,50) and (96,50), doormat (95,51), a big-box store standing in for Robin's carpenter shop,
    //    which is on the Mountain map the game does not have yet.
    // Every doormat is walkable and reachable on foot from the town's spawn (a test checks it); the shops are far enough apart that no two are in reach at once.
    private static readonly (int X, int Y) s_townSeedShopTile = (43, 57);
    private static readonly (int X, int Y) s_townClothingShopTile = (20, 89);
    private static readonly (int X, int Y) s_townFurnitureShopTile = (95, 51);

    // The blacksmith's counter: the doormat in front of the door at tile (94, 81), see MineWorld.
    private static readonly (int X, int Y) s_townBlacksmithTile = MineWorld.BlacksmithCounter;

    // The forty floors of the mine, one private per-player map each (docs/adr/ADR-026 D3). A floor's way up is its own arrival tile.
    private static readonly MapSpec[] s_mineFloors = BuildMineFloors();

    // The community board (docs/adr/ADR-030) is the billboard on the front of Pierre's store, tiles (41,56) and (42,56) ("Billboard" actions in Town.tmx); its counter is the walkable tile in front of
    // the right-hand sign, one tile west of the seed shop's doormat. The two are in reach of each other, so the nearest counter wins (ShopRules.TryFindShop).
    private static readonly (int X, int Y) s_townBoardTile = (42, 57);

    private static readonly MapSpec[] s_all =
    [
        new(Farm, "Farm.tmx", FarmProps.FarmhouseDoorFront.X, FarmProps.FarmhouseDoorFront.Y, FarmProps.Placements, [s_farmhouseDoor], [new MapWarp(s_farmhouseDoor.X, s_farmhouseDoor.Y, FarmHouse, 3, 11)], farmable: true, isPrivate: true, placeable: true),
        new(FarmHouse, "FarmHouse.tmx", 3, 11, isPrivate: true, placeable: true, indoors: true),
        new(BusStop, "BusStop.tmx", 11, 23),
        new(Forest, "Forest.tmx", 68, 0),
        new(Town, "Town.tmx", 4, 54, shops:
        [
            new ShopPoint(ShopCatalog.SeedShop, s_townSeedShopTile.X, s_townSeedShopTile.Y),
            new ShopPoint(ShopCatalog.ClothingShop, s_townClothingShopTile.X, s_townClothingShopTile.Y),
            new ShopPoint(ShopCatalog.FurnitureShop, s_townFurnitureShopTile.X, s_townFurnitureShopTile.Y),
            new ShopPoint(ShopCatalog.Blacksmith, s_townBlacksmithTile.X, s_townBlacksmithTile.Y),
            new ShopPoint(CommunityGoalCatalog.BoardCounterId, s_townBoardTile.X, s_townBoardTile.Y),
        ],
        extraWarps: [new MapWarp(CoopLand.TownGate.X, CoopLand.TownGate.Y, CoopFarm, CoopLand.LandArrival.X, CoopLand.LandArrival.Y), .. MineGateWarps()]),
        new(Backwoods, "Backwoods.tmx", 14, 38),

        // The mine's entrance cave: the file's exits to the Mountain are dropped (the world does not host it), ours lead to Town and down the shaft.
        new(Mine, "Mine.tmx", MineWorld.CaveShaftFront.X, MineWorld.CaveShaftFront.Y,
            openTiles: [MineWorld.CaveShaft],
            extraWarps:
            [
                new MapWarp(MineWorld.CaveShaft.X, MineWorld.CaveShaft.Y, RoomOfFloor(1), MineLayouts.For(1).SpawnX, MineLayouts.For(1).SpawnY),
                new MapWarp(MineWorld.CaveTownExit.X, MineWorld.CaveTownExit.Y, Town, MineWorld.TownArrival.X, MineWorld.TownArrival.Y),
            ],
            elevator: new ElevatorPoint(MineWorld.CaveElevator.X, MineWorld.CaveElevator.Y),
            indoors: true),

        // Co-op land reuses the placeholder Farm_Foraging.tmx (an open meadow, no buildings; same tilesheets as the farm, docs/adr/ADR-007). Its file exits to the bus stop, forest and backwoods stay.
        new(CoopFarm, "Farm_Foraging.tmx", CoopLand.LandArrival.X, CoopLand.LandArrival.Y, extraWarps: [new MapWarp(CoopLand.LandGate.X, CoopLand.LandGate.Y, Town, CoopLand.TownArrival.X, CoopLand.TownArrival.Y)], farmable: true, isCoop: true, placeable: true),
        .. s_mineFloors,
    ];

    /// <summary>Returns the id of a mine floor map, for example <c>mine7</c>.</summary>
    /// <param name="floor">The floor, 1 to <see cref="MineRules.MaxFloor"/>.</param>
    /// <returns>The id.</returns>
    public static string MineFloorId(int floor) => MineRules.FloorMapId(floor);

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

    private static string RoomOfFloor(int floor) => MineRules.FloorMapId(floor);

    // The Town edge that takes a player to the mine: five tiles of the north road, where the file's own exits to the Mountain would be.
    private static MapWarp[] MineGateWarps()
    {
        MapWarp[] warps = new MapWarp[MineWorld.TownGateToX - MineWorld.TownGateFromX + 1];
        for (int i = 0; i < warps.Length; i++)
        {
            warps[i] = new MapWarp(MineWorld.TownGateFromX + i, -1, Mine, MineWorld.CaveArrival.X, MineWorld.CaveArrival.Y);
        }

        return warps;
    }

    private static MapSpec[] BuildMineFloors()
    {
        MapSpec[] floors = new MapSpec[MineRules.MaxFloor];
        for (int floor = 1; floor <= floors.Length; floor++)
        {
            MineLayouts.Layout layout = MineLayouts.For(floor);

            // Up: to the floor above, or from the first floor to the cave in front of the shaft. The way up is the arrival tile itself.
            MapWarp up = floor == 1
                ? new MapWarp(layout.SpawnX, layout.SpawnY, Mine, MineWorld.CaveShaftFront.X, MineWorld.CaveShaftFront.Y)
                : new MapWarp(layout.SpawnX, layout.SpawnY, MineRules.FloorMapId(floor - 1), MineLayouts.For(floor - 1).SpawnX, MineLayouts.For(floor - 1).SpawnY);
            floors[floor - 1] = new MapSpec(
                MineRules.FloorMapId(floor),
                MineLayouts.FileOf(floor),
                layout.SpawnX,
                layout.SpawnY,
                extraWarps: [up],
                isPrivate: true,
                mineFloor: floor,
                indoors: true,
                elevator: MineRules.HasElevator(floor) ? new ElevatorPoint(layout.SpawnX, layout.SpawnY) : null);
        }

        return floors;
    }
}
