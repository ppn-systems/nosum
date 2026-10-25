// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Nodes;

/// <summary>
/// The game's day count, as far as resource nodes need it (docs/adr/ADR-021 D4). Today a day is a real UTC day; the season calendar
/// (docs/adr/ADR-018) replaces the implementation and calls <see cref="IResourceNodeService.AdvanceDayAsync"/> itself when a game day ends.
/// </summary>
public interface IGameDayClock
{
    /// <summary>Gets the number of the current game day; it only increases.</summary>
    long Today { get; }
}
