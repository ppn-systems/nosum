// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Weather;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// The pure farm rules: tilling, planting, watering, harvesting and crop growth. Time is a parameter (Unix milliseconds) and the calendar is
/// passed in, so the server and the client compute the same stage from the same stored numbers and tests need no clock. A crop grows by one day
/// for every game day its soil was watered, counted when that day ends (docs/adr/ADR-021 D7, <see cref="FarmTile"/>); bare tilled soil
/// that stays dry may revert to untilled (<see cref="IsReverted"/>). A rainy day waters every outdoor tile without any write (docs/adr/ADR-027 D2):
/// <see cref="IsWateredOn"/> is the one place that decides whether a day counts as watered, and every rule below asks it.
/// </summary>
public static class FarmRules
{
    /// <summary>Chance, in percent, that tilled soil without a crop reverts to untilled at the end of a dry game day.</summary>
    public const int RevertChancePercent = 30;

    /// <summary>Farthest distance from the player's feet to the centre of the target tile, world pixels.</summary>
    public const double MaxReachPixels = 32;

    /// <summary>Returns whether a tile centre is within reach of the player's feet.</summary>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileX">Target tile x.</param>
    /// <param name="tileY">Target tile y.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns><see langword="true"/> when the tile can be worked from there.</returns>
    public static bool InReach(double feetX, double feetY, int tileX, int tileY, int tileSize)
    {
        double dx = ((tileX + 0.5) * tileSize) - feetX;
        double dy = ((tileY + 0.5) * tileSize) - feetY;
        return (dx * dx) + (dy * dy) <= MaxReachPixels * MaxReachPixels;
    }

    /// <summary>Tills a tile.</summary>
    /// <param name="tillable">Whether the map allows tilling there.</param>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="next">The tilled tile when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Till(bool tillable, FarmTile? current, long nowMs, GameCalendar calendar, out FarmTile next)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = FarmTile.Bare;
        if (current is not null)
        {
            return FarmVerdict.AlreadyTilled;
        }

        if (!tillable)
        {
            return FarmVerdict.NotTillable;
        }

        next = FarmTile.Bare with { TendedDay = calendar.DayNumberAt(nowMs) };
        return FarmVerdict.Ok;
    }

    /// <summary>Plants a seed's crop on tilled soil that is empty or holds a withered crop, in a season the crop grows in.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="cropId">The crop to plant.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="next">The planted tile when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Plant(FarmTile? current, int cropId, long nowMs, GameCalendar calendar, out FarmTile next)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        if (tile.HasCrop && !IsWithered(tile, nowMs, calendar))
        {
            return FarmVerdict.HasCrop;
        }

        if (!CropCatalog.TryGet(cropId, out CropDefinition? crop))
        {
            return FarmVerdict.UnknownCrop;
        }

        if (!crop.GrowsIn(calendar.SeasonAt(nowMs)))
        {
            return FarmVerdict.OutOfSeason;
        }

        // Water from an earlier day belongs to the old crop; water poured today (before planting) still counts.
        int today = calendar.DayNumberAt(nowMs);
        next = new FarmTile(cropId, nowMs, 0, tile.WateredDay == today ? today : 0, 0);
        return FarmVerdict.Ok;
    }

    /// <summary>
    /// Waters tilled soil for the current game day: a day that already ended since the last watering is settled into the growth, then today is
    /// recorded. Watering twice on one day changes nothing.
    /// </summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="next">The watered tile when allowed.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it too (<see cref="IsWateredOn"/>).</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Water(FarmTile? current, long nowMs, GameCalendar calendar, out FarmTile next, bool outdoors = true, bool sprinkled = false)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        int today = calendar.DayNumberAt(nowMs);
        next = tile with { GrowthDays = tile.HasCrop ? GrowthDaysAt(tile, nowMs, calendar, outdoors, sprinkled) : 0, WateredDay = today };
        return FarmVerdict.Ok;
    }

    /// <summary>Clears tilled soil that holds no crop (a pickaxe), so the tile is plain ground again.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Clear(FarmTile? current)
        => current is not { } tile ? FarmVerdict.NotTilled : tile.HasCrop ? FarmVerdict.HasCrop : FarmVerdict.Ok;

    /// <summary>Harvests a mature crop that has not withered; the soil stays tilled.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="next">The bare tile when allowed.</param>
    /// <param name="crop">The crop that was harvested when allowed.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Harvest(FarmTile? current, long nowMs, GameCalendar calendar, out FarmTile next, out CropDefinition? crop)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = default;
        crop = null;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        if (!tile.HasCrop || !CropCatalog.TryGet(tile.CropId, out CropDefinition? found))
        {
            return FarmVerdict.NothingToDo;
        }

        if (IsWithered(tile, nowMs, calendar))
        {
            return FarmVerdict.Withered;
        }

        if (!IsMature(tile, nowMs, calendar))
        {
            return FarmVerdict.NotMature;
        }

        int today = calendar.DayNumberAt(nowMs);
        crop = found;
        next = new FarmTile(0, 0, 0, tile.WateredDay == today ? today : 0, today);
        return FarmVerdict.Ok;
    }

    /// <summary>
    /// Returns the watered game days a crop has reached, capped at what it needs to mature: the settled days, the last hand-watered day once it has
    /// ended, and every ended day after that which was watered by rain (<see cref="IsWateredOn"/>).
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile (<c>SprinklerRules</c>); outdoors it waters the tile every day.</param>
    /// <returns>Watered game days; zero for bare soil.</returns>
    public static int GrowthDaysAt(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, bool sprinkled = false)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (!tile.HasCrop || !CropCatalog.TryGet(tile.CropId, out CropDefinition? crop))
        {
            return 0;
        }

        int today = calendar.DayNumberAt(nowMs);

        // The last hand-watered day counts once it has ended.
        int days = tile.GrowthDays + (tile.WateredDay > 0 && tile.WateredDay < today ? 1 : 0);

        // Days up to the last watering are already settled (watering settles rain with it); later ones, or all of them since planting when the soil
        // was never watered, count when it rained. Stops as soon as the crop is mature, so a long gap costs nothing.
        if (outdoors && days < crop.TotalGrowthDays)
        {
            int first = tile.WateredDay > 0 ? tile.WateredDay + 1 : calendar.DayNumberAt(tile.PlantedAtMs);
            for (int day = first; day < today && days < crop.TotalGrowthDays; day++)
            {
                if (IsWateredOn(tile, day, calendar, outdoors, sprinkled))
                {
                    days++;
                }
            }
        }

        return Math.Min(crop.TotalGrowthDays, days);
    }

    /// <summary>
    /// Returns whether a game day counts as a watered day for a tile that was not hand-watered on it: outdoors, a rainy day waters every tilled tile
    /// (docs/adr/ADR-027 D2) and a sprinkler beside the tile waters it every day (docs/adr/ADR-024); indoors nothing does. This is the single place that decides it, so other sources (a sprinkler's reach) extend it here and the
    /// growth, the wet look and the reverting of bare soil all follow, on the server and the client alike.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="dayNumber">The game day number.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors.</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile. A sprinkler waters its four orthogonal tiles every day it stands; the state is read as it is now, not as it was on that day.</param>
    /// <returns><see langword="true"/> when the day waters the tile without anyone's hand.</returns>
    public static bool IsWateredOn(in FarmTile tile, int dayNumber, GameCalendar calendar, bool outdoors = true, bool sprinkled = false)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        _ = tile;
        return outdoors && (sprinkled || WeatherRules.WatersOn(calendar, dayNumber));
    }

    /// <summary>Returns the stage a tile's crop shows, from 0 (just planted) to mature.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile (<c>SprinklerRules</c>); outdoors it waters the tile every day.</param>
    /// <returns>The stage, or -1 for bare soil.</returns>
    public static int StageAt(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, bool sprinkled = false)
        => tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) ? crop.StageAt(GrowthDaysAt(tile, nowMs, calendar, outdoors, sprinkled)) : -1;

    /// <summary>Returns whether a tile's crop can be harvested.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile (<c>SprinklerRules</c>); outdoors it waters the tile every day.</param>
    /// <returns><see langword="true"/> when a mature crop grows there.</returns>
    public static bool IsMature(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, bool sprinkled = false)
        => tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) && GrowthDaysAt(tile, nowMs, calendar, outdoors, sprinkled) >= crop.TotalGrowthDays;

    /// <summary>
    /// Returns whether bare tilled soil has reverted to untilled: each game day that ended while the soil held no crop and was not watered
    /// has a <see cref="RevertChancePercent"/> chance of undoing the tilling (docs/adr/ADR-021 D7). The day the soil was tilled or its crop
    /// harvested, and each day it was watered, do not count. The roll is a pure hash of the instance, the tile and the day, so it needs no stored
    /// random state, gives the same answer wherever and whenever it is asked, and never reverses. Soil with a crop never reverts.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="instanceKey">The key of the map instance (<c>MapInstance.Key</c>) the tile is in.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors: a rainy day then counts as watered and does not roll.</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile: every day is then watered.</param>
    /// <returns><see langword="true"/> when the tile is no longer tilled.</returns>
    public static bool IsReverted(in FarmTile tile, string instanceKey, int tileX, int tileY, long nowMs, GameCalendar calendar, bool outdoors = true, bool sprinkled = false)
    {
        ArgumentNullException.ThrowIfNull(instanceKey);
        ArgumentNullException.ThrowIfNull(calendar);

        if (tile.HasCrop)
        {
            return false;
        }

        int today = calendar.DayNumberAt(nowMs);
        for (int day = Math.Max(tile.TendedDay, tile.WateredDay) + 1; day < today; day++)
        {
            if (!IsWateredOn(tile, day, calendar, outdoors, sprinkled) && RevertRoll(instanceKey, tileX, tileY, day) < RevertChancePercent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the deterministic roll, 0 to 99, that decides whether a dry tile reverts at the end of a day.</summary>
    /// <param name="instanceKey">The key of the map instance.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <param name="dayNumber">The day that ended.</param>
    /// <returns>A number from 0 to 99, evenly spread.</returns>
    public static int RevertRoll(string instanceKey, int tileX, int tileY, int dayNumber)
    {
        ArgumentNullException.ThrowIfNull(instanceKey);

        unchecked
        {
            // FNV-1a over the key (string.GetHashCode is randomized per process), then SplitMix64 rounds over the tile and the day.
            ulong hash = 14695981039346656037UL;
            foreach (char c in instanceKey)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }

            hash = Mix(hash ^ (((ulong)(uint)tileX << 32) | (uint)tileY));
            hash = Mix(hash ^ ((ulong)(uint)dayNumber * 0x9E3779B97F4A7C15UL));
            return (int)(hash % 100UL);
        }
    }

    /// <summary>
    /// Returns whether a tile's crop has withered: it was planted in a season it grows in, and the first later season it does not grow in
    /// has begun (docs/adr/ADR-018). Computed from the planting time alone, so no tick is needed and it never reverses; a mature crop
    /// that was not harvested in time withers too.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <returns><see langword="true"/> when a withered crop stands on the tile.</returns>
    public static bool IsWithered(in FarmTile tile, long nowMs, GameCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        return tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) && nowMs >= calendar.WitherAtMs(tile.PlantedAtMs, crop.Seasons);
    }

    /// <summary>Returns whether the soil is wet at a moment: it was watered during the current game day, or it rains on it.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where a rainy day wets it.</param>
    /// <param name="sprinkled">Whether a sprinkler stands beside the tile.</param>
    /// <returns><see langword="true"/> until the day ends.</returns>
    public static bool IsWet(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, bool sprinkled = false)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        int today = calendar.DayNumberAt(nowMs);
        return (tile.WateredDay > 0 && tile.WateredDay == today) || IsWateredOn(tile, today, calendar, outdoors, sprinkled);
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
