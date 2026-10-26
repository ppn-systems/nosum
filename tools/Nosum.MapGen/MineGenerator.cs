// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.MapGen;

/// <summary>
/// Generates the forty floors of the mine (docs/adr/ADR-042 D4). A floor is a cave grown from a fixed seed per floor around its arrival tile
/// (<see cref="MineLayouts"/>), so the same floor always gives the same bytes. The arrival is open and it lies in a cave of at least
/// <see cref="MinCave"/> tiles by construction; <see cref="Verify"/> then checks the floor the way the game reads it: the walk grid, the
/// way up, the elevator, and the stones, ore and ladder <see cref="MineFloorPlanner"/> places on several days.
/// </summary>
internal static class MineGenerator
{
    /// <summary>The name of the cave tileset in <c>art/own/source/tilesets</c>.</summary>
    public const string Tileset = "cave";

    /// <summary>The cave sheet's plain floor tile: the <c>Back</c> layer under every closed cell, and one of the floor variants on an open cell.</summary>
    public const int FloorTile = 0;

    /// <summary>The cave sheet's wall tile: the <c>Buildings</c> layer on a closed cell with no open side. It must not be <c>Passable</c>.</summary>
    public const int WallTile = 1;

    /// <summary>Tiles per side of a floor.</summary>
    public const int Size = 48;

    /// <summary>The smallest cave a floor may have: room for the stones, ore and ladder of a floor.</summary>
    public const int MinCave = 200;

    /// <summary>Stones a floor must place, as the planner's test requires.</summary>
    public const int MinStones = 6;

    // Tiles kept closed around the edge, so no cave reaches the map border.
    private const int Margin = 2;

    // Open tiles the walk carves before the cave is smoothed.
    private const int CarveTarget = 900;

    // Steps of the smoothing: each pass opens a tile that has at least five open neighbours.
    private const int SmoothPasses = 3;

    // New seeds tried when a cave fails the checks.
    private const int Attempts = 8;

    // Walking days the planner is checked on: the planner's placement depends on the seed, which the game derives from the day.
    private static readonly long[] s_days = [0, 1, 2];

    // Salt of the cave seeds, so they differ from every other seeded choice.
    private const ulong Salt = 0x4E6F73756D;

    // Salt of the dressing seeds: the floor variants and the decor of a floor.
    private const ulong DressSalt = 0x44726573;

    // The cave sheet's walkable tiles (cave.tileset): the floor variants, the rock decor, and the ladder-up and elevator markers.
    private static readonly int[] s_floorVariants = [FloorTile, 2, 3, 4];
    private static readonly int[] s_decor = [35, 39, 45, 46];
    private const int LadderUp = 32;
    private const int ElevatorLit = 34;

    // Share of the open cells that carry a decor tile instead of a floor variant, in percent.
    private const int DecorPercent = 6;

    // The wall edges: a closed cell next to open ground takes WallEdge plus its open sides (north 1, east 2, south 4, west 8).
    private const int WallEdge = 16;
    private const int WallEdges = 16;

    // The floors live in Maps/Mines, so the sheet is one folder up from the floor, as the .tmx names it.
    private const string SheetFromFloors = "../";

    private static readonly (int Dx, int Dy)[] s_steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Generates a floor.</summary>
    /// <param name="floor">The floor, 1 to <see cref="MineRules.MaxFloor"/>.</param>
    /// <param name="cave">The cave tileset, which must have a floor and a wall tile.</param>
    /// <returns>The <c>.tmx</c> document.</returns>
    /// <exception cref="InvalidDataException">The cave tileset has no usable floor or wall tile.</exception>
    /// <exception cref="InvalidOperationException">No seed gave a floor that passes <see cref="Verify"/>.</exception>
    public static string Generate(int floor, TilesetData cave)
    {
        ArgumentNullException.ThrowIfNull(cave);
        ArgumentOutOfRangeException.ThrowIfLessThan(floor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(floor, MineRules.MaxFloor);
        CheckTiles(cave);

        MineLayouts.Layout arrival = MineLayouts.For(floor);
        TilesetData placed = new(cave.Name, SheetFromFloors + cave.Image, cave.TileWidth, cave.TileHeight, cave.Columns, cave.TileCount, cave.Traits);
        IReadOnlyList<string> last = [];
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            bool[] open = Carve(NodeRandom.Mix(Salt, floor, attempt), arrival.SpawnX, arrival.SpawnY);
            string tmx = Write(open, placed, arrival.SpawnX, arrival.SpawnY, MineRules.HasElevator(floor), NodeRandom.Mix(DressSalt, floor, attempt));
            last = Verify(floor, tmx);
            if (last.Count == 0)
            {
                return tmx;
            }
        }

        throw new InvalidOperationException($"Floor {floor}: no cave passed the checks in {Attempts} attempts: {string.Join("; ", last)}");
    }

    /// <summary>Checks a generated floor the way the game reads it.</summary>
    /// <param name="floor">The floor the document is for.</param>
    /// <param name="tmx">The document.</param>
    /// <returns>What is wrong with it; empty when the floor passes.</returns>
    public static IReadOnlyList<string> Verify(int floor, string tmx)
    {
        ArgumentException.ThrowIfNullOrEmpty(tmx);

        List<string> problems = [];
        TileMap map;
        try
        {
            map = TmxReader.Parse(tmx);
        }
        catch (InvalidDataException ex)
        {
            problems.Add($"floor {floor} does not parse: {ex.Message}");
            return problems;
        }

        if (map.Width != Size || map.Height != Size)
        {
            problems.Add($"floor {floor} is {map.Width} x {map.Height} tiles, expected {Size} x {Size}");
        }

        if (!GameMaps.TryGet(MineRules.FloorMapId(floor), out MapSpec? spec))
        {
            problems.Add($"floor {floor} is not in the map catalogue");
            return problems;
        }

        MineLayouts.Layout arrival = MineLayouts.For(floor);
        int ax = arrival.SpawnX, ay = arrival.SpawnY;
        WalkGrid grid = spec.BuildWalkGrid(map);
        if (grid.IsBlocked(ax, ay))
        {
            problems.Add($"floor {floor}: the arrival tile ({ax}, {ay}) is blocked");
            return problems;
        }

        int cave = Reachable(grid, ax, ay).Count(r => r);
        if (cave < MinCave)
        {
            problems.Add($"floor {floor}: the arrival's cave has {cave} tiles, it needs {MinCave}");
        }

        if (spec.Elevator is { } elevator && (elevator.TileX != ax || elevator.TileY != ay))
        {
            problems.Add($"floor {floor}: the elevator does not stand on the arrival tile");
        }

        List<MapWarp> warps = spec.ResolveWarps(map);
        if (warps.Count != 1 || warps[0].FromX != ax || warps[0].FromY != ay)
        {
            problems.Add($"floor {floor}: the way up must be the one exit, on the arrival tile");
        }

        HashSet<(int X, int Y)> reserved = [(ax, ay), (ax, ay - 1)];
        foreach (long day in s_days)
        {
            MineFloorPlanner.Plan plan = MineFloorPlanner.Build(floor, MineRules.FloorSeed(Guid.Empty, day, floor), grid, reserved, ax, ay);
            bool[] reachable = Reachable(grid, ax, ay);
            string where = $"floor {floor}, day {day}";
            if (plan.Nodes.Count(n => n.Kind == NodeKind.Stone) < MinStones)
            {
                problems.Add($"{where}: fewer than {MinStones} stones");
            }

            if (plan.Nodes.Any(n => n.Kind == NodeKind.CopperOre) != MineRules.HasCopper(floor))
            {
                problems.Add($"{where}: copper ore does not match the floor's rule");
            }

            if (plan.Nodes.Any(n => n.Kind == NodeKind.IronOre) != MineRules.HasIron(floor))
            {
                problems.Add($"{where}: iron ore does not match the floor's rule");
            }

            if (MineRules.HasLadder(floor) != (plan.Ladder is not null))
            {
                problems.Add($"{where}: the ladder does not match the floor's rule");
            }

            if (plan.Nodes.Any(n => !reachable[(n.Y * grid.Width) + n.X]))
            {
                problems.Add($"{where}: a node lies where a player cannot walk");
            }
        }

        return problems;
    }

    // The walk carves an open path from the arrival, so the cave is connected to it; then the cave is smoothed and the arrival's
    // component is kept. The edge stays closed.
    private static bool[] Carve(ulong seed, int ax, int ay)
    {
        bool[] open = new bool[Size * Size];
        NodeRandom random = new(seed);
        int x = ax, y = ay, count = 1;
        open[Index(x, y)] = true;
        for (long step = 0; count < CarveTarget && step < 5_000_000; step++)
        {
            (int dx, int dy) = s_steps[random.Next(s_steps.Length)];
            x = Math.Clamp(x + dx, Margin, Size - 1 - Margin);
            y = Math.Clamp(y + dy, Margin, Size - 1 - Margin);
            if (!open[Index(x, y)])
            {
                open[Index(x, y)] = true;
                count++;
            }
        }

        for (int pass = 0; pass < SmoothPasses; pass++)
        {
            bool[] next = new bool[open.Length];
            for (int cy = 1; cy < Size - 1; cy++)
            {
                for (int cx = 1; cx < Size - 1; cx++)
                {
                    int around = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if ((dx != 0 || dy != 0) && open[Index(cx + dx, cy + dy)])
                            {
                                around++;
                            }
                        }
                    }

                    next[Index(cx, cy)] = around >= 5;
                }
            }

            open = next;
        }

        // The arrival and its four neighbours are always open, and the cave is what is connected to the arrival.
        open[Index(ax, ay)] = true;
        foreach ((int dx, int dy) in s_steps)
        {
            open[Index(ax + dx, ay + dy)] = true;
        }

        return KeepComponent(open, ax, ay);
    }

    private static bool[] KeepComponent(bool[] open, int ax, int ay)
    {
        bool[] keep = new bool[open.Length];
        Queue<(int X, int Y)> queue = new();
        keep[Index(ax, ay)] = true;
        queue.Enqueue((ax, ay));
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in s_steps)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= Size || ny >= Size || !open[Index(nx, ny)] || keep[Index(nx, ny)])
                {
                    continue;
                }

                keep[Index(nx, ny)] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return keep;
    }

    // The floor is dressed from its own seed: each open cell takes a floor variant, or rock decor on a few of them. The arrival tile
    // carries the way up (the ladder, or the lit elevator pad on an elevator floor). Closed cells take a wall edge by their open sides.
    private static string Write(bool[] open, TilesetData cave, int ax, int ay, bool elevator, ulong dressSeed)
    {
        int cells = Size * Size;
        NodeRandom random = new(dressSeed);
        int[] floorGids = new int[cells];
        int[] wallGids = new int[cells];
        int[] front = new int[cells];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = Index(x, y);
                if (!open[i])
                {
                    floorGids[i] = 1 + FloorTile;
                    wallGids[i] = 1 + WallTileAt(open, x, y);
                    continue;
                }

                int tile;
                if (x == ax && y == ay)
                {
                    tile = elevator ? ElevatorLit : LadderUp;
                }
                else if (random.Next(100) < DecorPercent)
                {
                    tile = s_decor[random.Next(s_decor.Length)];
                }
                else
                {
                    tile = s_floorVariants[random.Next(s_floorVariants.Length)];
                }

                floorGids[i] = 1 + tile;
            }
        }

        return TmxWriter.Write(
            Size,
            Size,
            [new PlacedTileset(cave, 1)],
            [new MapLayer("Back", floorGids), new MapLayer("Buildings", wallGids), new MapLayer("Front", front)],
            []);
    }

    // A closed cell is the plain wall when no side is open, and the wall edge of its open sides otherwise.
    private static int WallTileAt(bool[] open, int x, int y)
    {
        int sides = (IsOpen(open, x, y - 1) ? 1 : 0) | (IsOpen(open, x + 1, y) ? 2 : 0) | (IsOpen(open, x, y + 1) ? 4 : 0) | (IsOpen(open, x - 1, y) ? 8 : 0);
        return sides == 0 ? WallTile : WallEdge + sides;
    }

    private static bool IsOpen(bool[] open, int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && open[Index(x, y)];

    private static void CheckTiles(TilesetData cave)
    {
        if (cave.TileCount <= WallTile)
        {
            throw new InvalidDataException($"Tileset '{cave.Name}' needs a wall tile {WallTile}; it has {cave.TileCount} tiles.");
        }

        foreach (int tile in s_floorVariants.Concat(s_decor).Concat([LadderUp, ElevatorLit]))
        {
            CheckExists(cave, tile);
            cave.Traits.TryGetValue(tile, out TileTraits traits);
            if ((traits & (TileTraits.Water | TileTraits.Impassable)) != 0)
            {
                throw new InvalidDataException($"Tileset '{cave.Name}': the floor tile {tile} must be walkable.");
            }
        }

        foreach (int tile in Enumerable.Range(WallEdge, WallEdges).Append(WallTile))
        {
            CheckExists(cave, tile);
            cave.Traits.TryGetValue(tile, out TileTraits wall);
            if ((wall & TileTraits.Passable) != 0)
            {
                throw new InvalidDataException($"Tileset '{cave.Name}': the wall tile {tile} must not be Passable.");
            }
        }
    }

    private static void CheckExists(TilesetData cave, int tile)
    {
        if (tile >= cave.TileCount)
        {
            throw new InvalidDataException($"Tileset '{cave.Name}' needs tile {tile} for the floors; it has {cave.TileCount} tiles.");
        }
    }

    private static bool[] Reachable(WalkGrid grid, int startX, int startY)
    {
        bool[] seen = new bool[grid.Width * grid.Height];
        if (grid.IsBlocked(startX, startY))
        {
            return seen;
        }

        Queue<(int X, int Y)> queue = new();
        seen[(startY * grid.Width) + startX] = true;
        queue.Enqueue((startX, startY));
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in s_steps)
            {
                int nx = x + dx, ny = y + dy;
                if (grid.IsBlocked(nx, ny) || seen[(ny * grid.Width) + nx])
                {
                    continue;
                }

                seen[(ny * grid.Width) + nx] = true;
                queue.Enqueue((nx, ny));
            }
        }

        return seen;
    }

    private static int Index(int x, int y) => (y * Size) + x;
}
