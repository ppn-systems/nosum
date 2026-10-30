// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Journal;

/// <summary>One discovered item of a player's collection journal.</summary>
/// <param name="ItemId">The item id from the item catalogue.</param>
/// <param name="Count">How many of the item the player has obtained in total.</param>
/// <param name="FirstObtainedAt">When the player first obtained the item.</param>
public sealed record JournalLine(int ItemId, int Count, DateTimeOffset FirstObtainedAt);

/// <summary>
/// The collection journal: which items a player has obtained, how many, and when first. Discoveries are written by the inventory service in the same
/// transaction as the grant (no feature calls this to record); this service only reads them.
/// </summary>
public interface IJournalService
{
    /// <summary>Reads the player's journal: the discovered items only, lowest item id first.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One line per discovered item; empty for a player who has obtained nothing.</returns>
    Task<IReadOnlyList<JournalLine>> GetAsync(Guid playerId, CancellationToken cancellationToken = default);
}
