// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using Nosum.Abstractions.Accounts;

namespace Nosum.Infrastructure.Players;

/// <summary>
/// The place a player left a moment ago, kept in memory for <see cref="Window"/> (issue #172). A quick reconnect (a browser refresh) must resume where the
/// player stood, not at an older database copy: the leave's write runs behind the connection close, so a join can start before it lands. The record is
/// written the moment the player leaves the map, and it is handed out once.
/// </summary>
public sealed class RecentPlaces
{
    /// <summary>How long after leaving a place is still resumed from memory.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, (PlayerStateRow Row, DateTimeOffset LeftAt)> _places = new();
    private readonly TimeProvider _time;

    /// <summary>Initializes the store.</summary>
    /// <param name="time">The clock the window is measured on; tests pass a fake one.</param>
    public RecentPlaces(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Records where the player left, replacing any earlier record.</summary>
    /// <param name="accountId">The player's account.</param>
    /// <param name="place">The place the player stood in.</param>
    public void Remember(Guid accountId, PlayerStateRow place) => _places[accountId] = (place, _time.GetUtcNow());

    /// <summary>Takes the place the player left within <see cref="Window"/>; the record is removed either way.</summary>
    /// <param name="accountId">The player's account.</param>
    /// <param name="place">The place, when this returns <see langword="true"/>.</param>
    /// <returns>Whether a place was left recently enough to resume.</returns>
    public bool TryTake(Guid accountId, out PlayerStateRow place)
    {
        if (_places.TryRemove(accountId, out (PlayerStateRow Row, DateTimeOffset LeftAt) left)
            && _time.GetUtcNow() - left.LeftAt < Window)
        {
            place = left.Row;
            return true;
        }

        place = default;
        return false;
    }
}
