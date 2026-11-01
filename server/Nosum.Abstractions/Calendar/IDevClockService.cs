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
}
