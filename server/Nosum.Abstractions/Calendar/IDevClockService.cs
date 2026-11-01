// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Calendar;

/// <summary>
/// Development-only tester tool: moves the server's game clock forward so crop growth and day rollovers can be tested without waiting twelve
/// hours per day. The host registers it, and the handler that exposes it, only in the Development environment.
/// </summary>
public interface IDevClockService
{
    /// <summary>Largest jump in one call, game days.</summary>
    const int MaxDays = 60;

    /// <summary>Largest number of season starts passed in one call.</summary>
    const int MaxSeasons = 4;

    /// <summary>Moves the game clock forward and delivers the day-ended events of every day it crossed.</summary>
    /// <param name="days">Whole game days, 1 to <see cref="MaxDays"/>.</param>
    /// <returns>The server clock after the move, Unix milliseconds; <see langword="null"/> when <paramref name="days"/> is out of range.</returns>
    long? AdvanceDays(int days);

    /// <summary>Moves the game clock to the start of the next season, once per season asked for, and delivers the day-ended events it crossed.
    /// Zero moves it to the next Spring (0 to 3 passes; no pass when the season is already Spring), so a QA run starts from a known season (docs/adr/ADR-079 D3).</summary>
    /// <param name="seasons">How many season starts to pass, 1 to <see cref="MaxSeasons"/>; zero for the next Spring.</param>
    /// <returns>The server clock after the move, Unix milliseconds; <see langword="null"/> when <paramref name="seasons"/> is out of range.</returns>
    long? AdvanceSeasons(int seasons);

    /// <summary>Pins the game clock so it stops moving, or lets it run again from the instant it was pinned at (docs/adr/ADR-079 amendment 2026-11-01).
    /// A pinned clock keeps the season and the day of the run; <see cref="AdvanceDays"/> and <see cref="AdvanceSeasons"/> still move it.</summary>
    /// <param name="frozen"><see langword="true"/> to pin the clock; <see langword="false"/> to let it run.</param>
    /// <returns>The server clock after the change, Unix milliseconds.</returns>
    long FreezeClock(bool frozen);

    /// <summary>Moves the game clock to the start of the next day with sun, when today is not sunny (docs/adr/ADR-079 amendment 2026-11-01). Weather is a
    /// pure function of the calendar and the day (docs/adr/ADR-027 D1), so the tester moves the day to reach sun; it does not override the weather.
    /// Delivers the day-ended events it crossed, like <see cref="AdvanceDays"/>.</summary>
    /// <returns>The server clock after the move, Unix milliseconds.</returns>
    long SkipToSunnyDay();
}
