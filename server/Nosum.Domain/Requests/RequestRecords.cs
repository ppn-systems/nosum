// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Requests;

/// <summary>
/// One request of one game day on the Town board (docs/adr/ADR-036 D6): server-wide, written once from the day's deterministic roll, never rewritten.
/// The key is (day, slot).
/// </summary>
public sealed class DailyRequest
{
    /// <summary>Gets or sets the game day number (<c>GameCalendar.DayNumberAt</c>).</summary>
    public int Day { get; set; }

    /// <summary>Gets or sets the request's place in its day, from zero.</summary>
    public int Slot { get; set; }

    /// <summary>Gets or sets the item that is delivered.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets how many items.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the coins the delivery pays, fixed when the row was written.</summary>
    public long RewardCoins { get; set; }

    /// <summary>Gets or sets when the row was written.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A player filled a request of a day (docs/adr/ADR-036 D7). The key is (day, slot, player): the primary key is what makes a second fill impossible.
/// </summary>
public sealed class RequestFill
{
    /// <summary>Gets or sets the game day number of the request.</summary>
    public int Day { get; set; }

    /// <summary>Gets or sets the request's slot in its day.</summary>
    public int Slot { get; set; }

    /// <summary>Gets or sets the player who filled it.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the coins that were paid.</summary>
    public long RewardCoins { get; set; }

    /// <summary>Gets or sets when it was filled.</summary>
    public DateTimeOffset FilledAt { get; set; }
}
