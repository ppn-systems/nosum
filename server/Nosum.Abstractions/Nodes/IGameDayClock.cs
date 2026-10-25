// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Abstractions.Nodes;

/// <summary>
/// The game's day count, as far as resource nodes need it (docs/adr/ADR-021 D4): the day number on the server calendar (docs/adr/ADR-018).
/// The calendar has no "day ended" event; a job watches this clock and calls <see cref="IResourceNodeService.AdvanceDayAsync"/>.
/// </summary>
public interface IGameDayClock
{
    /// <summary>Gets the number of the current game day; it only increases.</summary>
    long Today { get; }

    /// <summary>Gets how long one game day lasts in real time.</summary>
    TimeSpan DayLength { get; }
}
