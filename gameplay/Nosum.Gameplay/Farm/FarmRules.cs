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
    /// <summary>Game days in a row bare tilled soil may stay dry before it reverts to untilled (docs/adr/ADR-021 D7, amended 2026-10-31).</summary>
    public const int DryDaysToRevert = 3;

    /// <summary>Game days in a row a planted crop may go unwatered before it withers (docs/adr/ADR-021 D7, amended 2026-10-31).</summary>
    public const int DryDaysToWither = 2;

    /// <summary>Chance, in percent, that a harvest of an outdoor crop on a rainy day, or the day after rain, yields one more unit (docs/adr/ADR-027 D8).</summary>
    public const int BonusHarvestChancePercent = 20;

    // Keeps the bonus roll apart from the revert roll for the same tile and day.
    private const ulong BonusSalt = 0xB0BA5E5A17UL;

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
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands.</param>
    public static FarmVerdict Plant(FarmTile? current, int cropId, long nowMs, GameCalendar calendar, out FarmTile next, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        if (tile.HasCrop && !IsWithered(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
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
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Water(FarmTile? current, long nowMs, GameCalendar calendar, out FarmTile next, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        // A dead crop is not revived by water; refusing keeps its wither a fixed fact of the past (docs/adr/ADR-021 D7, amended).
        if (tile.HasCrop && IsWithered(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
        {
            return FarmVerdict.Withered;
        }

        int today = calendar.DayNumberAt(nowMs);
        next = tile with { GrowthDays = tile.HasCrop ? GrowthDaysAt(tile, nowMs, calendar, outdoors, sprinklerSinceMs) : 0, WateredDay = today };
        return FarmVerdict.Ok;
    }

    /// <summary>Returns the tile offsets watered by a sprinkler item (<c>ItemCatalog.Sprinkler</c> waters 4 orthogonal tiles).</summary>
    /// <param name="itemId">Placed item id.</param>
    /// <returns>The relative tile offsets watered by that sprinkler, or an empty span when the item is not a sprinkler.</returns>
    public static ReadOnlySpan<(int Dx, int Dy)> GetSprinklerOffsets(int itemId)
        => Placeables.SprinklerRules.GetSprinklerOffsets(itemId);

    /// <summary>Clears tilled soil that holds no crop (a pickaxe), so the tile is plain ground again.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Clear(FarmTile? current)
        => current is not { } tile ? FarmVerdict.NotTilled : tile.HasCrop ? FarmVerdict.HasCrop : FarmVerdict.Ok;

    /// <summary>Clears tilled soil that holds no live crop (bare soil or a withered crop), so the tile is plain ground again.</summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands.</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Clear(FarmTile? current, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return current is not { } tile
            ? FarmVerdict.NotTilled
            : tile.HasCrop && !IsWithered(tile, nowMs, calendar, outdoors, sprinklerSinceMs)
                ? FarmVerdict.HasCrop
                : FarmVerdict.Ok;
    }

    /// <summary>
    /// Cuts a withered crop down with a scythe (docs/adr/ADR-018): the dead plant goes and the soil stays tilled, bare, ready for a new seed. The day
    /// it was cut counts as tended, like a harvest, so the bare soil starts its dry days again from there.
    /// </summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="next">The tilled bare soil left behind when allowed.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands.</param>
    /// <returns>The verdict: <see cref="FarmVerdict.NothingToDo"/> when no withered crop stands there.</returns>
    public static FarmVerdict CutWithered(FarmTile? current, long nowMs, GameCalendar calendar, out FarmTile next, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        next = default;
        if (current is not { } tile)
        {
            return FarmVerdict.NotTilled;
        }

        if (!IsWithered(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
        {
            return FarmVerdict.NothingToDo;
        }

        int today = calendar.DayNumberAt(nowMs);
        next = new FarmTile(0, 0, 0, tile.WateredDay == today ? today : 0, today);
        return FarmVerdict.Ok;
    }

    /// <summary>
    /// Harvests a mature crop that has not withered; the soil stays tilled. A regrowing crop (<see cref="CropDefinition.RegrowDays"/>) stays
    /// planted and needs its regrow days of watering before it bears again; any other crop leaves bare soil.
    /// </summary>
    /// <param name="current">The tile now, or <see langword="null"/> when untilled.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="next">The tile after the harvest when allowed: bare soil, or the regrowing plant.</param>
    /// <param name="crop">The crop that was harvested when allowed.</param>
    /// <param name="outdoors">Whether the tile is outdoors, so rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns>The verdict.</returns>
    public static FarmVerdict Harvest(FarmTile? current, long nowMs, GameCalendar calendar, out FarmTile next, out CropDefinition? crop, bool outdoors = true, long? sprinklerSinceMs = null)
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

        if (IsWithered(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
        {
            return FarmVerdict.Withered;
        }

        if (!IsMature(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
        {
            return FarmVerdict.NotMature;
        }

        int today = calendar.DayNumberAt(nowMs);
        crop = found;
        int watered = tile.WateredDay == today ? today : 0;
        // A regrowing plant starts counting again from the harvest: rain before it was spent on the crop just picked (docs/adr/ADR-027 D2).
        next = found.Regrows
            ? new FarmTile(tile.CropId, nowMs, found.GrowthDaysAfterHarvest, watered, 0)
            : new FarmTile(0, 0, 0, watered, today);
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
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns>Watered game days; zero for bare soil.</returns>
    public static int GrowthDaysAt(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
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
        if ((outdoors || sprinklerSinceMs is not null) && days < crop.TotalGrowthDays)
        {
            int first = tile.WateredDay > 0 ? tile.WateredDay + 1 : calendar.DayNumberAt(tile.PlantedAtMs);
            for (int day = first; day < today && days < crop.TotalGrowthDays; day++)
            {
                if (IsWateredOn(tile, day, calendar, outdoors, sprinklerSinceMs))
                {
                    days++;
                }
            }
        }

        return Math.Min(crop.TotalGrowthDays, days);
    }

    /// <summary>
    /// Returns whether a game day counts as a watered day for a tile that was not hand-watered on it: a rainy day waters every tilled tile outdoors
    /// (docs/adr/ADR-027 D2), and a sprinkler beside the tile waters it every day after the one it was placed on (docs/adr/ADR-024), indoors too. This is the single place that decides it, so the growth, the wet look and the reverting of bare soil all follow, on the server and the client alike.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="dayNumber">The game day number.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns><see langword="true"/> when the day waters the tile without anyone's hand.</returns>
    public static bool IsWateredOn(in FarmTile tile, int dayNumber, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        _ = tile;
        return SprinklerWatersOn(sprinklerSinceMs, dayNumber, calendar) || (outdoors && WeatherRules.WatersOn(calendar, dayNumber));
    }

    // A sprinkler placed on day N waters the days after N, from the start of each (docs/adr/ADR-024 D4): a day it was not standing through is never watered by it.
    private static bool SprinklerWatersOn(long? sprinklerSinceMs, int dayNumber, GameCalendar calendar)
        => sprinklerSinceMs is { } since && dayNumber > calendar.DayNumberAt(since);

    /// <summary>Returns the stage a tile's crop shows, from 0 (just planted) to mature.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns>The stage, or -1 for bare soil.</returns>
    public static int StageAt(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
        => tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) ? crop.StageAt(GrowthDaysAt(tile, nowMs, calendar, outdoors, sprinklerSinceMs)) : -1;

    /// <summary>Returns whether a tile's crop can be harvested.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns><see langword="true"/> when a mature crop grows there.</returns>
    public static bool IsMature(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
        => tile.HasCrop && CropCatalog.TryGet(tile.CropId, out CropDefinition? crop) && GrowthDaysAt(tile, nowMs, calendar, outdoors, sprinklerSinceMs) >= crop.TotalGrowthDays;

    /// <summary>
    /// Returns whether bare tilled soil has reverted to untilled: <see cref="DryDaysToRevert"/> game days in a row ended without water after the
    /// tilling or the last watering (docs/adr/ADR-021 D7, amended 2026-10-31). A watered day is not dry: hand watering, rain outdoors and a sprinkler
    /// beside the tile all restart the run. The day the soil was tilled or its crop harvested is a day of grace. Soil with a crop never reverts.
    /// The answer depends only on the stored days, the calendar and the weather, so reads and the day sweep agree, and it never reverses.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors: a rainy day then counts as watered.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands.</param>
    /// <returns><see langword="true"/> when the tile is no longer tilled.</returns>
    public static bool IsReverted(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (tile.HasCrop)
        {
            return false;
        }

        return HasDryRun(tile, Math.Max(tile.TendedDay, tile.WateredDay), calendar.DayNumberAt(nowMs), DryDaysToRevert, calendar, outdoors, sprinklerSinceMs);
    }

    // True when `length` game days in a row ended after `afterDay` and before `today` were all dry. A dry day is one nobody watered by hand and that
    // neither rained (outdoors) nor was sprinkled; the run restarts at every watered day.
    private static bool HasDryRun(in FarmTile tile, int afterDay, int today, int length, GameCalendar calendar, bool outdoors, long? sprinklerSinceMs)
    {
        int run = 0;
        for (int day = Math.Max(afterDay, 0) + 1; day < today; day++)
        {
            run = IsWateredOn(tile, day, calendar, outdoors, sprinklerSinceMs) ? 0 : run + 1;
            if (run >= length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a harvest on this tile yields one bonus unit (docs/adr/ADR-027 D8): an outdoor tile harvested on a rainy day or the day
    /// after rain, and a deterministic roll for the tile and the day under <see cref="BonusHarvestChancePercent"/>. Nothing is stored.
    /// </summary>
    /// <param name="instanceKey">The key of the map instance.</param>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <returns><see langword="true"/> when the harvest yields one more unit.</returns>
    public static bool IsBonusHarvest(string instanceKey, int tileX, int tileY, long nowMs, GameCalendar calendar, bool outdoors)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (!outdoors)
        {
            return false;
        }

        int today = calendar.DayNumberAt(nowMs);
        bool rainy = WeatherRules.WatersOn(calendar, today) || (today > 1 && WeatherRules.WatersOn(calendar, today - 1));
        return rainy && BonusRoll(instanceKey, tileX, tileY, today) < BonusHarvestChancePercent;
    }

    // 0 to 99, the roll behind IsBonusHarvest; its own salt keeps it apart from any other roll on the same tile and day.
    private static int BonusRoll(string instanceKey, int tileX, int tileY, int dayNumber)
    {
        unchecked
        {
            ulong hash = Mix(TileHash(instanceKey, tileX, tileY) ^ ((ulong)(uint)dayNumber * 0x9E3779B97F4A7C15UL) ^ BonusSalt);
            return (int)(hash % 100UL);
        }
    }

    // FNV-1a over the key (string.GetHashCode is randomized per process), then a SplitMix64 round over the tile.
    private static ulong TileHash(string instanceKey, int tileX, int tileY)
    {
        ArgumentNullException.ThrowIfNull(instanceKey);

        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in instanceKey)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }

            return Mix(hash ^ (((ulong)(uint)tileX << 32) | (uint)tileY));
        }
    }

    /// <summary>
    /// Returns whether a tile's crop has withered (docs/adr/ADR-018, docs/adr/ADR-021 D7 amended 2026-10-31). Two causes, both fixed facts of the past
    /// so that no tick is needed and it never reverses: the first later season the crop does not grow in has begun while the crop was not ripe, or
    /// <see cref="DryDaysToWither"/> game days in a row ended after the planting or the last hand watering without rain or a sprinkler, before the crop
    /// ripened. A ripe crop (<see cref="IsMature"/>) waiting for its harvest is kept through both: later dry days do not dry it out, and the season change
    /// does not wither it (docs/adr/ADR-018 D4, amended: a ripe crop waits for the player). Ripeness is judged at the instant of the season change, so a
    /// crop that was not ripe then stays withered even if rain ripens it later. Watering a withered crop is refused, so the dry run cannot be erased
    /// afterwards.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands.</param>
    /// <returns><see langword="true"/> when a withered crop stands on the tile.</returns>
    public static bool IsWithered(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (!tile.HasCrop || !CropCatalog.TryGet(tile.CropId, out CropDefinition? crop))
        {
            return false;
        }

        // The season change withers a crop that is not ripe when it comes (ADR-018 D4). Ripeness is read at that instant: a ripe crop never withers
        // from the season, and a crop unripe then stays withered for good, whatever the rain does afterwards.
        long witherAtMs = calendar.WitherAtMs(tile.PlantedAtMs, crop.Seasons);
        if (nowMs >= witherAtMs && !IsMature(tile, witherAtMs, calendar, outdoors, sprinklerSinceMs))
        {
            return true;
        }

        WalkCrop(tile, crop, calendar.DayNumberAt(nowMs), calendar, outdoors, sprinklerSinceMs, out bool withered, out _, out _);
        return withered;
    }

    /// <summary>
    /// Returns whether one more dry game day would wither a crop or revert bare soil, so the tile is thirsty: the soil is not wet today, and the dry
    /// run that ends yesterday is one day short of the limit. A ripe crop is never at risk. The caller excludes a crop already withered by the season
    /// (<see cref="IsWithered"/> covers the dry run itself, and the server's withered flag covers the season).
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where rain waters it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands.</param>
    /// <returns><see langword="true"/> when the tile needs water today or it will wither or revert at the day's end.</returns>
    public static bool IsAtRisk(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (IsWet(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
        {
            return false;
        }

        int today = calendar.DayNumberAt(nowMs);
        if (tile.HasCrop)
        {
            if (!CropCatalog.TryGet(tile.CropId, out CropDefinition? crop))
            {
                return false;
            }

            WalkCrop(tile, crop, today, calendar, outdoors, sprinklerSinceMs, out bool withered, out int dryDays, out bool ripe);
            return !withered && !ripe && dryDays == DryDaysToWither - 1;
        }

        if (IsReverted(tile, nowMs, calendar, outdoors, sprinklerSinceMs))
        {
            return false;
        }

        int afterDay = Math.Max(tile.TendedDay, tile.WateredDay);
        return TrailingDryDays(tile, afterDay, today, DryDaysToRevert, calendar, outdoors, sprinklerSinceMs) == DryDaysToRevert - 1;
    }

    // Walks the game days a crop has been growing since its last hand watering (or its planting), counting growth the way GrowthDaysAt does, and reports
    // whether a dry run has withered it before it ripened, the dry days in a row that end yesterday, and whether it has ripened. Stops at the first answer
    // that cannot change: a wither, or the ripening (a later dry day cannot undo either).
    private static void WalkCrop(in FarmTile tile, CropDefinition crop, int today, GameCalendar calendar, bool outdoors, long? sprinklerSinceMs, out bool withered, out int dryDays, out bool ripe)
    {
        int planted = calendar.DayNumberAt(tile.PlantedAtMs);
        int after = Math.Max(tile.WateredDay, planted);
        int growth = tile.WateredDay > 0 ? tile.GrowthDays : 0;
        int run = 0;
        withered = false;
        ripe = false;
        for (int day = tile.WateredDay > 0 ? tile.WateredDay : planted; day < today; day++)
        {
            if (day == tile.WateredDay || IsWateredOn(tile, day, calendar, outdoors, sprinklerSinceMs))
            {
                growth = Math.Min(crop.TotalGrowthDays, growth + 1);
                run = 0;
            }
            else if (day > after)
            {
                run++;
            }

            ripe = growth >= crop.TotalGrowthDays;
            if (ripe || run >= DryDaysToWither)
            {
                withered = !ripe;
                break;
            }
        }

        dryDays = run;
    }

    // The dry game days in a row that end yesterday, counted back to the day after afterDay and stopping at limit.
    private static int TrailingDryDays(in FarmTile tile, int afterDay, int today, int limit, GameCalendar calendar, bool outdoors, long? sprinklerSinceMs)
    {
        int run = 0;
        for (int day = today - 1; day > Math.Max(afterDay, 0) && run < limit; day--)
        {
            if (IsWateredOn(tile, day, calendar, outdoors, sprinklerSinceMs))
            {
                break;
            }

            run++;
        }

        return run;
    }

    /// <summary>Returns whether the soil is wet at a moment: it was watered during the current game day, or it rains on it.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="nowMs">Time to evaluate at, Unix milliseconds.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="outdoors">Whether the tile is outdoors, where a rainy day wets it.</param>
    /// <param name="sprinklerSinceMs">When a sprinkler beside the tile was placed, Unix milliseconds; null when none stands. It waters the game days after the day it was placed (<c>SprinklerRules</c>).</param>
    /// <returns><see langword="true"/> until the day ends.</returns>
    public static bool IsWet(in FarmTile tile, long nowMs, GameCalendar calendar, bool outdoors = true, long? sprinklerSinceMs = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        int today = calendar.DayNumberAt(nowMs);
        return (tile.WateredDay > 0 && tile.WateredDay == today) || IsWateredOn(tile, today, calendar, outdoors, sprinklerSinceMs);
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
