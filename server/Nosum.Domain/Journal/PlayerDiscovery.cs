// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Journal;

/// <summary>
/// One entry of a player's collection journal: how many of an item the player has obtained, and when the first one came (the journal is kept by
/// item, so a player has at most one row per item). The key is the player and the item.
/// </summary>
public sealed class PlayerDiscovery
{
    /// <summary>Gets or sets the player who obtained the item.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the item id from the item catalogue.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets how many of the item the player has obtained in total; never below one.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets when the player first obtained the item. Set once, when the row is created, and never moved.</summary>
    public DateTimeOffset FirstObtainedAt { get; set; }

    /// <summary>Starts the journal entry for an item the player has not obtained before.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many were obtained; at least one.</param>
    /// <param name="obtainedAt">When they were obtained; becomes the first-obtained time.</param>
    /// <returns>The new entry.</returns>
    public static PlayerDiscovery Start(Guid playerId, int itemId, int quantity, DateTimeOffset obtainedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        return new PlayerDiscovery { PlayerId = playerId, ItemId = itemId, Count = quantity, FirstObtainedAt = obtainedAt };
    }

    /// <summary>Adds obtained items to the count. The first-obtained time is left as it is.</summary>
    /// <param name="quantity">How many were obtained; at least one. The count saturates at <see cref="int.MaxValue"/>.</param>
    public void Obtain(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        this.Count = (int)Math.Min(int.MaxValue, (long)this.Count + quantity);
    }
}
