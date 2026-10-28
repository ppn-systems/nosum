// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Mines;

/// <summary>
/// The rules of the mine (docs/adr/ADR-026 D3): how many floors, which ore lies where, which floors have an elevator and how deep a player may
/// ride. Pure; the server decides with it and the client shows the same choices.
/// </summary>
public static class MineRules
{
    /// <summary>The deepest floor of the mine in M2.</summary>
    public const int MaxFloor = 40;

    /// <summary>A floor with a number divisible by this has an elevator (and unlocks it when first reached).</summary>
    public const int ElevatorEvery = 5;

    /// <summary>Copper ore lies on floors 1 to this one.</summary>
    public const int CopperMaxFloor = 39;

    /// <summary>Iron ore lies on this floor and below.</summary>
    public const int IronMinFloor = 20;

    /// <summary>The "floor" of the entrance cave: the elevator stop that leads out to the first floor's shaft.</summary>
    public const int EntranceFloor = 0;

    /// <summary>Special stop id sent from the mine elevator / way-up dialog to return directly to the player's farmhouse.</summary>
    public const int ReturnHomeStop = 255;

    /// <summary>Returns the map id of a floor, for example <c>mine7</c>.</summary>
    /// <param name="floor">The floor, 1 to <see cref="MaxFloor"/>.</param>
    /// <returns>The id.</returns>
    public static string FloorMapId(int floor) => "mine" + floor.ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns whether a floor has an elevator.</summary>
    /// <param name="floor">The floor.</param>
    /// <returns><see langword="true"/> for floors 5, 10, 15 ... 40.</returns>
    public static bool HasElevator(int floor) => floor >= ElevatorEvery && floor <= MaxFloor && floor % ElevatorEvery == 0;

    /// <summary>Returns whether a floor holds copper ore nodes.</summary>
    /// <param name="floor">The floor.</param>
    /// <returns><see langword="true"/> on floors 1 to 39.</returns>
    public static bool HasCopper(int floor) => floor >= 1 && floor <= CopperMaxFloor;

    /// <summary>Returns whether a floor holds iron ore nodes.</summary>
    /// <param name="floor">The floor.</param>
    /// <returns><see langword="true"/> on floors 20 to 40.</returns>
    public static bool HasIron(int floor) => floor >= IronMinFloor && floor <= MaxFloor;

    /// <summary>Returns whether a floor hides a ladder to the next one: every floor but the last.</summary>
    /// <param name="floor">The floor.</param>
    /// <returns><see langword="true"/> on floors 1 to 39.</returns>
    public static bool HasLadder(int floor) => floor >= 1 && floor < MaxFloor;

    /// <summary>Returns the stops the elevator offers a player: the entrance and every elevator floor down to the deepest floor they reached.</summary>
    /// <param name="deepest">The deepest floor the player reached, zero for none.</param>
    /// <returns>The floors, entrance first.</returns>
    public static List<int> ElevatorStops(int deepest)
    {
        List<int> stops = [EntranceFloor];
        for (int floor = ElevatorEvery; floor <= Math.Min(deepest, MaxFloor); floor += ElevatorEvery)
        {
            stops.Add(floor);
        }

        return stops;
    }

    /// <summary>Returns whether a player who reached a depth may ride the elevator to a floor.</summary>
    /// <param name="deepest">The deepest floor they reached.</param>
    /// <param name="target">The stop asked for: <see cref="EntranceFloor"/> or an elevator floor.</param>
    /// <returns><see langword="true"/> when the stop exists and they have been that deep.</returns>
    public static bool CanRide(int deepest, int target)
        => target == EntranceFloor || (HasElevator(target) && target <= deepest);

    /// <summary>
    /// Returns the seed a floor's contents are built from: the player, the game day and the floor. The same three always give the same floor, so a
    /// player who leaves and returns the same day finds the same stones, and nobody else shares it.
    /// </summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="day">The game day number.</param>
    /// <param name="floor">The floor.</param>
    /// <returns>The seed.</returns>
    public static ulong FloorSeed(Guid playerId, long day, int floor)
        => NodeRandom.Mix(NodeRandom.HashOf(playerId.ToString("N")), day, floor);
}
