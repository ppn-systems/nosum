// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Calendar;

/// <summary>
/// The server-wide calendar (pillar 3, docs/adr/ADR-018): pure functions from a UTC instant (Unix milliseconds) to a year, a season,
/// a day and a time of day. The calendar is fully described by an epoch (the start of year 1, spring, day 1) and the length of
/// a game day in real time, so the client rebuilds the same calendar from those two numbers and computes the clock locally.
/// Instants before the epoch count as the epoch. A season is <see cref="DaysPerSeason"/> game days; a year is four seasons.
/// </summary>
public sealed class GameCalendar
{
    /// <summary>Game days in one season.</summary>
    public const int DaysPerSeason = 28;

    /// <summary>
    /// Tells whether a season begins after <paramref name="fromDay"/> and no later than <paramref name="toDay"/>. A season begins on every day number
    /// that is a multiple of <see cref="DaysPerSeason"/>, so a catch-up across several days sees a season change exactly when the live day rollover would.
    /// </summary>
    /// <param name="fromDay">The day the state was last brought up to.</param>
    /// <param name="toDay">The day to bring it up to.</param>
    /// <returns><see langword="true"/> when a season began in between.</returns>
    public static bool SeasonBeganBetween(long fromDay, long toDay) => toDay / DaysPerSeason > fromDay / DaysPerSeason;

    /// <summary>Seasons in one year.</summary>
    public const int SeasonsPerYear = 4;

    /// <summary>Game minutes in one game day.</summary>
    public const int MinutesPerDay = 24 * 60;

    /// <summary>Real length of one game day in the default calendar: 12 hours, so a season is 14 real days (owner: two weeks).</summary>
    public const long DefaultDayLengthMs = 12L * 60 * 60 * 1000;

    /// <summary>Start of year 1 in the default calendar: Monday 2026-11-02 00:00 UTC. The host may override it.</summary>
    public static readonly long DefaultEpochMs = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>Initializes a calendar.</summary>
    /// <param name="epochMs">Start of year 1, spring, day 1, 00:00, Unix milliseconds.</param>
    /// <param name="dayLengthMs">Real length of one game day, milliseconds; at least one per game minute.</param>
    public GameCalendar(long epochMs, long dayLengthMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dayLengthMs, MinutesPerDay);

        this.EpochMs = epochMs;
        this.DayLengthMs = dayLengthMs;
    }

    /// <summary>Gets the default calendar (docs/adr/ADR-018).</summary>
    public static GameCalendar Default { get; } = new(DefaultEpochMs, DefaultDayLengthMs);

    /// <summary>Gets the start of year 1, Unix milliseconds.</summary>
    public long EpochMs { get; }

    /// <summary>Gets the real length of one game day, milliseconds.</summary>
    public long DayLengthMs { get; }

    /// <summary>Gets the real length of one season, milliseconds.</summary>
    public long SeasonLengthMs => this.DayLengthMs * DaysPerSeason;

    /// <summary>Converts an instant to a calendar date.</summary>
    /// <param name="unixMs">The instant, Unix milliseconds (UTC).</param>
    /// <returns>The date; the first moment of year 1 for an instant before the epoch.</returns>
    public GameDate At(long unixMs)
    {
        long sinceEpoch = Math.Max(0, unixMs - this.EpochMs);
        long dayIndex = sinceEpoch / this.DayLengthMs;
        long msIntoDay = sinceEpoch % this.DayLengthMs;
        long seasonIndex = dayIndex / DaysPerSeason;
        return new GameDate(
            (int)((seasonIndex / SeasonsPerYear) + 1),
            (Season)(seasonIndex % SeasonsPerYear),
            (int)((dayIndex % DaysPerSeason) + 1),
            (int)(msIntoDay * MinutesPerDay / this.DayLengthMs));
    }

    /// <summary>
    /// Returns the number of the game day an instant falls in: 1 for the first game day since the epoch (also for any instant before it), then 2, 3, and so on without
    /// end (it does not reset at a season or year). Crop growth counts days by this number (docs/adr/ADR-021 D7); zero is never a day, so it can mean "no day" in stored state.
    /// </summary>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The day number, at least 1.</returns>
    public int DayNumberAt(long unixMs) => (int)Math.Min(int.MaxValue - 1, (Math.Max(0, unixMs - this.EpochMs) / this.DayLengthMs) + 1);

    /// <summary>Returns the instant a game day starts.</summary>
    /// <param name="dayNumber">The day number, at least 1.</param>
    /// <returns>The start of that day, Unix milliseconds.</returns>
    public long DayStartMs(int dayNumber) => this.EpochMs + ((Math.Max(1, dayNumber) - 1L) * this.DayLengthMs);

    /// <summary>Returns the instant the next game day starts, which is when the day that contains an instant ends.</summary>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The start of the following game day, Unix milliseconds.</returns>
    public long NextDayStartMs(long unixMs) => this.DayStartMs(this.DayNumberAt(unixMs) + 1);

    /// <summary>Returns the season at an instant.</summary>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The season.</returns>
    public Season SeasonAt(long unixMs) => (Season)(this.SeasonIndexAt(unixMs) % SeasonsPerYear);

    /// <summary>
    /// Returns which season an instant is in as a running count: 0 for the first spring since the epoch (also for any instant before it), then 1, 2, and so on without end
    /// (it does not reset at a year). Community goals are kept per season number (docs/adr/ADR-030).
    /// </summary>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The season number, at least 0.</returns>
    public int SeasonNumberAt(long unixMs) => (int)Math.Min(int.MaxValue - 1, this.SeasonIndexAt(unixMs));

    /// <summary>Returns the instant the season that contains an instant started.</summary>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The start of that season, Unix milliseconds.</returns>
    public long SeasonStartMs(long unixMs) => this.StartOfSeasonIndex(this.SeasonIndexAt(unixMs));

    /// <summary>Returns the instant the next season starts.</summary>
    /// <param name="unixMs">The instant, Unix milliseconds.</param>
    /// <returns>The start of the following season, Unix milliseconds.</returns>
    public long NextSeasonStartMs(long unixMs) => this.StartOfSeasonIndex(this.SeasonIndexAt(unixMs) + 1);

    /// <summary>
    /// Returns when a crop planted at an instant withers: the start of the first later season it does not grow in.
    /// A crop that grows in every season never withers. A crop planted in a season it does not grow in has already withered.
    /// </summary>
    /// <param name="plantedAtMs">When the crop was planted, Unix milliseconds.</param>
    /// <param name="grows">The seasons the crop grows in.</param>
    /// <returns>The withering instant, Unix milliseconds; <see cref="long.MaxValue"/> when it never withers.</returns>
    public long WitherAtMs(long plantedAtMs, SeasonSet grows)
    {
        long planted = this.SeasonIndexAt(plantedAtMs);
        if (!grows.Contains((Season)(planted % SeasonsPerYear)))
        {
            return plantedAtMs;
        }

        for (int step = 1; step <= SeasonsPerYear; step++)
        {
            if (!grows.Contains((Season)((planted + step) % SeasonsPerYear)))
            {
                return this.StartOfSeasonIndex(planted + step);
            }
        }

        return long.MaxValue;
    }

    private long SeasonIndexAt(long unixMs) => Math.Max(0, unixMs - this.EpochMs) / this.SeasonLengthMs;

    private long StartOfSeasonIndex(long seasonIndex) => this.EpochMs + (seasonIndex * this.SeasonLengthMs);
}
