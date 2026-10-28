// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Requests;

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
