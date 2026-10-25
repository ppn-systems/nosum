// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Calendar;

namespace Nosum.Abstractions.Calendar;

/// <summary>
/// The server calendar (pillar 3, docs/adr/ADR-018): the one <see cref="GameCalendar"/> every player lives in, and the server's clock.
/// Services that decide by season (planting, the seed shop) read both from here or take them by constructor.
/// </summary>
public interface ICalendarService
{
    /// <summary>Gets the calendar.</summary>
    GameCalendar Calendar { get; }

    /// <summary>Gets the server's clock now, Unix milliseconds.</summary>
    /// <returns>The current instant.</returns>
    long NowMs();
}
