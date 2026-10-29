// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Buildings;

/// <summary>Where a building stands in its construction.</summary>
public enum BuildingState : byte
{
    /// <summary>Still being built; it cannot be entered.</summary>
    UnderConstruction = 0,

    /// <summary>Finished: animals live here and players may walk in.</summary>
    Finished = 1,
}

/// <summary>Why a building could not be commissioned or an animal bought; the handler maps it to a wire status.</summary>
public enum BuildingVerdict
{
    /// <summary>Allowed.</summary>
    Ok = 0,

    /// <summary>The kind is not a building.</summary>
    UnknownBuilding = 1,

    /// <summary>The building is still under construction.</summary>
    NotFinished = 2,

    /// <summary>The building is full.</summary>
    Full = 3,

    /// <summary>The species does not live in that kind of building.</summary>
    WrongResident = 4,

    /// <summary>The name is not allowed.</summary>
    BadName = 5,

    /// <summary>The land holds as many buildings as it may.</summary>
    LimitReached = 6,
}

/// <summary>
/// Rules of farm buildings (docs/adr/ADR-028 D1, D2): the construction time is two game days from the moment the blueprint is placed, computed from the stored
/// timestamp and the calendar on every read, never ticked; and which instance a building's interior is.
/// </summary>
public static class BuildingRules
{
    /// <summary>Most buildings one piece of land (a farm, a co-op's land) may hold.</summary>
    public const int MaxPerLand = 8;

    /// <summary>Returns when a building placed at <paramref name="placedAtMs"/> is finished.</summary>
    /// <param name="placedAtMs">Server time the blueprint was placed, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar, which says how long a game day lasts.</param>
    /// <returns>The completion time, Unix milliseconds.</returns>
    public static long CompletesAtMs(long placedAtMs, GameCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        return placedAtMs + (BuildingCatalog.ConstructionDays * calendar.DayLengthMs);
    }

    /// <summary>Tells whether a building is finished.</summary>
    /// <param name="completesAtMs">The stored completion time, Unix milliseconds.</param>
    /// <param name="nowMs">Server time now, Unix milliseconds.</param>
    /// <returns><see cref="BuildingState.Finished"/> from the completion time on.</returns>
    public static BuildingState StateOf(long completesAtMs, long nowMs) => nowMs >= completesAtMs ? BuildingState.Finished : BuildingState.UnderConstruction;

    /// <summary>Gets the milliseconds until a building is finished.</summary>
    /// <param name="completesAtMs">The stored completion time, Unix milliseconds.</param>
    /// <param name="nowMs">Server time now, Unix milliseconds.</param>
    /// <returns>Zero once finished; never more than <see cref="int.MaxValue"/>.</returns>
    public static int RemainingMs(long completesAtMs, long nowMs) => (int)Math.Clamp(completesAtMs - nowMs, 0, int.MaxValue);

    /// <summary>Returns the instance of a building's interior.</summary>
    /// <param name="kind">The building's kind.</param>
    /// <param name="buildingId">The building's id.</param>
    /// <returns>The instance, keyed <c>coop@id</c> or <c>barn@id</c>.</returns>
    public static MapInstance InteriorOf(BuildingKind kind, Guid buildingId)
    {
        if (!BuildingCatalog.TryGet(kind, out BuildingDefinition? definition))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new MapInstance(definition.Key, buildingId);
    }

    /// <summary>Returns a placed building's door tile (inside its footprint, opened for walking) and the tile in front of it, where a player arrives when leaving.</summary>
    /// <param name="definition">The building.</param>
    /// <param name="tileX">Left tile of the placed footprint.</param>
    /// <param name="tileY">Top tile of the placed footprint.</param>
    /// <returns>The door tile and the tile in front of it.</returns>
    public static ((int X, int Y) Door, (int X, int Y) Front) DoorTiles(BuildingDefinition definition, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(definition);

        (int X, int Y) door = (tileX + definition.DoorX, tileY + definition.DoorY);
        return (door, (door.X, door.Y + 1));
    }

    /// <summary>Tells whether a building may take one more animal.</summary>
    /// <param name="definition">The building.</param>
    /// <param name="species">The species to add.</param>
    /// <param name="state">The construction state.</param>
    /// <param name="animals">Animals living there now.</param>
    /// <returns><see cref="BuildingVerdict.Ok"/> or the reason for refusing.</returns>
    public static BuildingVerdict CanHouse(BuildingDefinition definition, Animals.AnimalKind species, BuildingState state, int animals)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (state != BuildingState.Finished)
        {
            return BuildingVerdict.NotFinished;
        }

        if (!definition.Houses(species))
        {
            return BuildingVerdict.WrongResident;
        }

        return animals >= definition.Capacity ? BuildingVerdict.Full : BuildingVerdict.Ok;
    }
}
