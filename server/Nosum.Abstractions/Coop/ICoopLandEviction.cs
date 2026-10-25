// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Coop;

/// <summary>Moves a player out of co-op land when their membership ends (docs/adr/ADR-020).</summary>
public interface ICoopLandEviction
{
    /// <summary>
    /// If the account is standing on co-op land, takes them back to Town; otherwise does nothing. Called after a leave, a kick or a dissolution
    /// has been saved, so the player cannot act on the land afterwards (farm actions also re-check membership).
    /// </summary>
    /// <param name="accountId">The account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the player is out (or was never in).</returns>
    ValueTask EvictAsync(Guid accountId, CancellationToken cancellationToken = default);
}
