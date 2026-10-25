// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Shop;

namespace Nosum.Abstractions.Shops;

/// <summary>
/// Buying from a shop for coins (docs/adr/ADR-013 amendments). The server prices the purchase from <c>ShopCatalog</c>, checks that the
/// player's own presence record stands at a shop point of the shop, and moves coins and items in one transfer, so a purchase either happens
/// completely or not at all.
/// </summary>
public interface IShopService
{
    /// <summary>Buys one stack-sized purchase.</summary>
    /// <param name="connection">The player's connection (their position is the server's own record).</param>
    /// <param name="playerId">The player's account.</param>
    /// <param name="shopId">The shop.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status and the coins spent (zero unless Ok).</returns>
    Task<ShopOutcome> BuyAsync(IConnection connection, Guid playerId, int shopId, int itemId, int quantity, CancellationToken cancellationToken = default);
}

/// <summary>Result of a purchase.</summary>
/// <param name="Status">The outcome code.</param>
/// <param name="Spent">Coins spent; zero unless Ok.</param>
public readonly record struct ShopOutcome(ShopStatus Status, long Spent);
