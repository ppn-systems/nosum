// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Trade;
using Nosum.Gameplay.Trade;

namespace Nosum.Abstractions.Trade;

/// <summary>
/// Direct trade between two players on the same map (docs/adr/ADR-013): an invitation, an open session where both sides set an
/// offer, two confirmations, and one atomic swap. Sessions live in memory; a disconnect or leaving the map ends them. Nothing moves
/// before the swap, so aborting needs no refund; the swap itself re-checks every claim inside its transaction.
/// </summary>
public interface ITradeService
{
    /// <summary>Invites a player on the same map.</summary>
    /// <param name="connection">The asking player's connection.</param>
    /// <param name="playerId">The asking player.</param>
    /// <param name="targetMapPlayerId">The other player's id on the map.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<TradeStatus> RequestAsync(IConnection connection, Guid playerId, uint targetMapPlayerId, CancellationToken cancellationToken = default);

    /// <summary>Answers the invitation the player holds.</summary>
    /// <param name="playerId">The invited player.</param>
    /// <param name="accept">Whether to accept.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<TradeStatus> RespondAsync(Guid playerId, bool accept, CancellationToken cancellationToken = default);

    /// <summary>Replaces the player's side of the open trade; both confirmations are cleared.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="lines">The offered item lines.</param>
    /// <param name="coins">The offered coins.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<TradeStatus> SetOfferAsync(Guid playerId, IReadOnlyList<TradeLine> lines, long coins, CancellationToken cancellationToken = default);

    /// <summary>Confirms the offers as they stand; when both sides confirmed, swaps.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<TradeStatus> ConfirmAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the open trade, or declines the invitation the player holds or sent.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<TradeStatus> CancelAsync(Guid playerId, CancellationToken cancellationToken = default);
}
