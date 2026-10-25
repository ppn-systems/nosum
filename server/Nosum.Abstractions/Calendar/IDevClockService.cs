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

    /// <summary>Moves the game clock forward and delivers the day-ended events of every day it crossed.</summary>
    /// <param name="days">Whole game days, 1 to <see cref="MaxDays"/>.</param>
    /// <returns>The server clock after the move, Unix milliseconds; <see langword="null"/> when <paramref name="days"/> is out of range.</returns>
    long? AdvanceDays(int days);
}
