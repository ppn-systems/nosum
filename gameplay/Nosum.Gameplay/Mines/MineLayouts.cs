// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Mines;

/// <summary>
/// The floor plans of the mine (docs/adr/ADR-026 D3): which <c>Maps/Mines/*.tmx</c> file each floor uses and the tile a player arrives on. A
/// floor keeps its plan; what is rebuilt every game day is what stands on it (stones, ore, the ladder). The arrival tiles were
/// chosen as the open ground nearest the middle of the largest connected cave of each file, and a test checks they are walkable.
/// </summary>
public static class MineLayouts
{
    /// <summary>A floor plan.</summary>
    /// <param name="File">Number of the Tiled file in <c>Maps/Mines</c>.</param>
    /// <param name="SpawnX">Arrival tile x.</param>
    /// <param name="SpawnY">Arrival tile y.</param>
    public readonly record struct Layout(int File, int SpawnX, int SpawnY);

    // Floors 1-30 use the brown-rock plans, 31-39 the dark ones and the last floor the desert one (the sheet is the file's own).
    private static readonly Layout[] s_floors =
    [
        new(1, 10, 11),
        new(2, 23, 18),
        new(3, 19, 20),
        new(4, 16, 18),
        new(5, 19, 19),
        new(6, 21, 23),
        new(7, 27, 25),
        new(8, 28, 11),
        new(9, 22, 20),
        new(10, 10, 9),
        new(11, 20, 15),
        new(12, 37, 9),
        new(13, 17, 19),
        new(14, 23, 21),
        new(15, 19, 19),
        new(16, 25, 14),
        new(17, 15, 26),
        new(18, 20, 23),
        new(19, 25, 27),
        new(20, 13, 13),
        new(21, 13, 17),
        new(22, 21, 16),
        new(23, 20, 28),
        new(24, 33, 21),
        new(25, 25, 16),
        new(26, 15, 16),
        new(27, 26, 14),
        new(28, 26, 27),
        new(29, 27, 38),
        new(41, 19, 21),
        new(31, 15, 21),
        new(32, 22, 33),
        new(33, 14, 16),
        new(34, 21, 15),
        new(35, 18, 8),
        new(36, 18, 19),
        new(37, 25, 14),
        new(38, 18, 17),
        new(39, 5, 31),
        new(40, 20, 18),
    ];

    /// <summary>Gets every floor plan, floor 1 first.</summary>
    public static ReadOnlySpan<Layout> All => s_floors;

    /// <summary>Returns the plan of a floor.</summary>
    /// <param name="floor">The floor, 1 to <see cref="MineRules.MaxFloor"/>.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The floor is not one the mine has.</exception>
    public static Layout For(int floor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(floor, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(floor, MineRules.MaxFloor);
        return s_floors[floor - 1];
    }

    /// <summary>Returns the map file name of a floor, relative to the maps folder.</summary>
    /// <param name="floor">The floor.</param>
    /// <returns>For example <c>Mines/17.tmx</c>.</returns>
    public static string FileOf(int floor) => "Mines/" + For(floor).File.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmx";
}
