// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Market;

namespace Nosum.Abstractions.Market;

/// <summary>One listing as the market reports it.</summary>
/// <param name="ListingId">The listing id.</param>
/// <param name="SellerId">The seller's account (never sent to clients).</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Price">Price for the whole stack, coins.</param>
/// <param name="ExpiresAt">When the listing expires.</param>
/// <param name="SellerNickname">The seller's nickname.</param>
public sealed record ListingView(Guid ListingId, Guid SellerId, int ItemId, int Quantity, long Price, DateTimeOffset ExpiresAt, string SellerNickname);

/// <summary>One page of listings.</summary>
/// <param name="ServerNow">The server clock when read.</param>
/// <param name="Page">Page number, zero based.</param>
/// <param name="Total">Number of matching listings, all pages.</param>
/// <param name="Listings">The page.</param>
public sealed record ListingsPageView(DateTimeOffset ServerNow, int Page, int Total, IReadOnlyList<ListingView> Listings);

/// <summary>The sales of one item on one game day.</summary>
/// <param name="Day">The game day number.</param>
/// <param name="Min">The lowest unit price, coins.</param>
/// <param name="Median">The median unit price, coins.</param>
/// <param name="Max">The highest unit price, coins.</param>
/// <param name="Sales">How many sales the day had.</param>
public sealed record PriceDayView(int Day, long Min, long Median, long Max, int Sales);

/// <summary>The recent price history of one item (docs/adr/ADR-030 D4).</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Today">The game day number now.</param>
/// <param name="Days">The days that had sales, oldest first.</param>
public sealed record PriceHistoryView(int ItemId, int Today, IReadOnlyList<PriceDayView> Days);

/// <summary>Outcome of shipping items at the bin.</summary>
/// <param name="Status">Ok, or why nothing was sold.</param>
/// <param name="Earned">Coins paid.</param>
public readonly record struct ShipOutcome(MarketStatus Status, long Earned);

/// <summary>
/// Coins from the shipping bin and the consignment market: list a stack (items go into escrow), browse, buy, cancel, and the expiry
/// sweep. Every purchase and every return of items is one atomic transfer (docs/adr/ADR-013).
/// </summary>
public interface IMarketService
{
    /// <summary>Sells items from an inventory slot to the shipping bin at the fixed price table; the player must stand at the bin.</summary>
    /// <param name="connection">The player's connection (their position is read from the server's presence record).</param>
    /// <param name="playerId">The player.</param>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="itemId">The item the player expects in the slot.</param>
    /// <param name="quantity">How many; zero or less ships the whole stack.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    Task<ShipOutcome> ShipAsync(IConnection connection, Guid playerId, int slot, int itemId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Lists a stack for sale.</summary>
    /// <param name="playerId">The seller.</param>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="itemId">The item the seller expects in the slot.</param>
    /// <param name="quantity">How many of the stack.</param>
    /// <param name="price">Price for the whole stack.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<MarketStatus> CreateListingAsync(Guid playerId, int slot, int itemId, int quantity, long price, CancellationToken cancellationToken = default);

    /// <summary>Buys a listing: buyer coins to seller (minus the fee), items to buyer.</summary>
    /// <param name="buyerId">The buyer.</param>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<MarketStatus> BuyAsync(Guid buyerId, Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the player's own listing; the items go back to the inventory.</summary>
    /// <param name="playerId">The seller.</param>
    /// <param name="listingId">The listing.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<MarketStatus> CancelAsync(Guid playerId, Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>Reads one page of active listings, newest first.</summary>
    /// <param name="page">Page number, zero based.</param>
    /// <param name="itemIds">Only these items; empty for all.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The page.</returns>
    Task<ListingsPageView> BrowseAsync(int page, IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Reads the player's own active listings.</summary>
    /// <param name="playerId">The seller.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The listings, newest first.</returns>
    Task<ListingsPageView> MineAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Reads the daily price history of one item: the min, median and max unit price of the sales of each of the last game days that had any.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The history; no days for an item that never sold.</returns>
    Task<PriceHistoryView> GetPriceHistoryAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>Returns the items of listings past their expiry to their sellers (the hosted job calls this; a seller with a full inventory is retried on the next sweep).</summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many listings were returned.</returns>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);
}
