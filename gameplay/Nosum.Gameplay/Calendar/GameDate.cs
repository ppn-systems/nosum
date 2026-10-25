// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Calendar;

/// <summary>A moment on the server calendar.</summary>
/// <param name="Year">Year, from 1.</param>
/// <param name="Season">Season of the year.</param>
/// <param name="DayOfSeason">Day of the season, from 1 to <see cref="GameCalendar.DaysPerSeason"/>.</param>
/// <param name="MinuteOfDay">Game minute of the day, from 0 (midnight) to 1439.</param>
public readonly record struct GameDate(int Year, Season Season, int DayOfSeason, int MinuteOfDay)
{
    /// <summary>Gets the game hour, 0 to 23.</summary>
    public int Hour => this.MinuteOfDay / 60;

    /// <summary>Gets the minute of the game hour, 0 to 59.</summary>
    public int Minute => this.MinuteOfDay % 60;
}
