// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.Abstractions.Economy;

/// <summary>
/// Where the market fee goes: the one seam between "a fee was charged" and "what happens to those coins" (docs/adr/ADR-013 amendments).
/// Owner decision 2026-10-25: the fee is a coin sink (anti-inflation), so the only implementation destroys the coins. If the fee ever funds
/// something (a community goal), another implementation returns legs that credit its holder and nothing else changes.
/// </summary>
public interface IFeeSink
{
    /// <summary>Gives the transfer legs that receive a fee inside the same transaction as the sale.</summary>
    /// <param name="fee">The fee in coins; zero when the price was too low to charge one.</param>
    /// <param name="reason">The audit code of the transfer, for example <c>market.buy</c>.</param>
    /// <returns>The legs to append to the transfer; empty when the coins simply leave the economy.</returns>
    IReadOnlyList<TransferLeg> Collect(long fee, string reason);
}
