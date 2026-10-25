// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Abstractions.Calendar;

/// <summary>A game day that has just ended (docs/adr/ADR-021 D7).</summary>
/// <param name="DayNumber">The day that ended: the 1-based count of game days since the calendar epoch (<c>GameCalendar.DayNumberAt</c>).</param>
/// <param name="EndedAtMs">The instant the day ended, Unix milliseconds on the server clock.</param>
public readonly record struct GameDayEnded(int DayNumber, long EndedAtMs);

/// <summary>
/// The day-ended seam: services subscribe to be told when a game day rolls over (resource-node regrowth, sprinklers, wither sweeps). It is for
/// side effects only. Game state (crop growth, wetness, bare-soil reverts) is computed lazily from stored day numbers and stays correct when no
/// event is delivered, for example after a restart or a missed tick, so a subscriber must never be the only place a rule lives.
/// One timer aligned to the calendar's day boundaries drives it for the whole server.
/// </summary>
public interface IDayClock
{
    /// <summary>
    /// Raised once for every game day that ends, in order, on a timer thread. A handler must return quickly and start its own async work;
    /// an exception in one handler is swallowed and does not stop the others or the clock.
    /// </summary>
    event Action<GameDayEnded>? DayEnded;

    /// <summary>Gets the game day number now.</summary>
    int CurrentDay { get; }
}
