// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;

namespace Nosum.Gameplay.Weather;

/// <summary>
/// The weather as a pure function of the calendar epoch and the game day number (docs/adr/ADR-027 D1): a seeded hash rolled against the season's
/// chances. Nothing is stored and nothing is sent; the server and every client compute the same weather from the calendar they already share.
/// The first <see cref="SunnyDaysAtSeasonStart"/> days of every season are always sunny.
/// </summary>
public static class WeatherRules
{
    /// <summary>Days at the start of each season that are always <see cref="WeatherKind.Sun"/>.</summary>
    public const int SunnyDaysAtSeasonStart = 2;

    // Chances in percent per season, indexed by Season: rain, storm, snow. The rest of the 100 is sun.
    private static readonly (int Rain, int Storm, int Snow)[] s_chances =
    [
        (20, 0, 0),  // spring
        (12, 5, 0),  // summer
        (20, 0, 0),  // fall
        (0, 0, 25),  // winter
    ];

    /// <summary>Returns the chances, in percent, of rain, storm and snow on a day of a season.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The chances; sun is the remainder.</returns>
    public static (int Rain, int Storm, int Snow) ChancesOf(Season season) => s_chances[(int)season];

    /// <summary>Returns the weather of a game day.</summary>
    /// <param name="calendar">The calendar; its epoch seeds the roll, so two servers with different epochs have different weather.</param>
    /// <param name="dayNumber">The game day number, 1-based (<see cref="GameCalendar.DayNumberAt"/>); less than 1 counts as 1.</param>
    /// <returns>The weather.</returns>
    public static WeatherKind ForDay(GameCalendar calendar, int dayNumber)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        int index = Math.Max(1, dayNumber) - 1;
        int dayOfSeason = (index % GameCalendar.DaysPerSeason) + 1;
        if (dayOfSeason <= SunnyDaysAtSeasonStart)
        {
            return WeatherKind.Sun;
        }

        (int rain, int storm, int snow) = s_chances[(index / GameCalendar.DaysPerSeason) % GameCalendar.SeasonsPerYear];
        int roll = Roll(calendar.EpochMs, index);
        if (roll < storm)
        {
            return WeatherKind.Storm;
        }

        if (roll < storm + rain)
        {
            return WeatherKind.Rain;
        }

        return roll < storm + rain + snow ? WeatherKind.Snow : WeatherKind.Sun;
    }

    /// <summary>Returns the weather at an instant.</summary>
    /// <param name="calendar">The calendar.</param>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The weather of the game day the instant falls in.</returns>
    public static WeatherKind At(GameCalendar calendar, long unixMs)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        return ForDay(calendar, calendar.DayNumberAt(unixMs));
    }

    /// <summary>Returns whether a kind of weather waters outdoor tilled soil (docs/adr/ADR-027 D2).</summary>
    /// <param name="weather">The weather.</param>
    /// <returns><see langword="true"/> for rain and storm.</returns>
    public static bool Waters(WeatherKind weather) => weather is WeatherKind.Rain or WeatherKind.Storm;

    /// <summary>Returns whether a game day waters outdoor tilled soil.</summary>
    /// <param name="calendar">The calendar.</param>
    /// <param name="dayNumber">The game day number.</param>
    /// <returns><see langword="true"/> when it rains that day.</returns>
    public static bool WatersOn(GameCalendar calendar, int dayNumber) => Waters(ForDay(calendar, dayNumber));

    // 0 to 99, a SplitMix64 round over the epoch and the day (no process-randomized hash, so every machine agrees).
    private static int Roll(long epochMs, int dayIndex)
    {
        unchecked
        {
            ulong hash = Mix((ulong)epochMs ^ 0x57EA7E12D00DUL);
            hash = Mix(hash ^ ((ulong)(uint)dayIndex * 0x9E3779B97F4A7C15UL));
            return (int)(hash % 100UL);
        }
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
